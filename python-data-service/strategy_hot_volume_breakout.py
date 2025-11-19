#!/usr/bin/env python
"""
基于 AKShare 的热点题材成交量放大策略回测脚本。

策略条件：
1. 成交量放大：最近 3 个交易日平均成交量 >= 过去 10 个交易日平均成交量 * 1.5
2. 股价站上短期均线：收盘价 > MA5 且 收盘价 > MA10
3. 板块热点：仅选市场最热的 TOP-N 题材，且个股需是该题材的 TOP-K 成员
4. 当日换手率 >= 5%
5. K 线为实体阳线，不允许冲高回落：收盘价>开盘价，实体占比充足且上影线不过长

使用方式：
    python strategy_hot_volume_breakout.py --top-hot 60 --themes 3 --theme-members 3
"""

from __future__ import annotations

import argparse
import math
import os
import sys
import time
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from typing import Dict, Iterable, List, Optional

import akshare as ak
import pandas as pd

if sys.platform.startswith('win'):
    try:
        sys.stdout.reconfigure(encoding='utf-8')  # type: ignore[attr-defined]
        sys.stderr.reconfigure(encoding='utf-8')  # type: ignore[attr-defined]
    except Exception:  # pylint: disable=broad-except
        pass

PROXY_VARS = ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy', 'ALL_PROXY', 'all_proxy']


def disable_global_proxy() -> Dict[str, Optional[str]]:
    """临时移除代理，返回被移除的环境变量以便恢复。"""
    removed = {}
    for key in PROXY_VARS:
        if key in os.environ:
            removed[key] = os.environ.pop(key)
    os.environ.setdefault('NO_PROXY', '*')
    os.environ.setdefault('no_proxy', '*')
    return removed


def restore_global_proxy(removed: Dict[str, Optional[str]]) -> None:
    """恢复之前移除的代理环境变量。"""
    for key, value in removed.items():
        if value is not None:
            os.environ[key] = value


def fetch_with_retry(func, description: str, *args, retries: int = 3, delay: float = 1.5, **kwargs):
    """统一的重试逻辑。"""
    last_exc = None
    for attempt in range(1, retries + 1):
        try:
            data = func(*args, **kwargs)
            if data is not None:
                return data
        except Exception as exc:  # pylint: disable=broad-except
            last_exc = exc
            print(f"[WARN] {description} 失败 (尝试 {attempt}/{retries}): {exc}")
            if attempt < retries:
                time.sleep(delay * attempt)
    raise RuntimeError(f"无法获取 {description}") from last_exc


def normalize_symbol(code: str) -> str:
    """统一股票代码到 ak.stock_zh_a_daily 需要的格式。"""
    if not code:
        raise ValueError("股票代码为空")
    clean = code.strip().upper()
    if clean.startswith(('SZ', 'SH')):
        prefix = clean[:2].lower()
        digits = clean[2:]
    else:
        digits = clean
        prefix = 'sh' if digits.startswith('6') else 'sz'
    if len(digits) != 6 or not digits.isdigit():
        raise ValueError(f"无效股票代码: {code}")
    return f"{prefix}{digits}"


def rename_columns(df: pd.DataFrame, new_names: Iterable[str]) -> pd.DataFrame:
    """辅助函数，按顺序重命名列，忽略缺失列。"""
    df = df.copy()
    for old, new in zip(df.columns, new_names):
        df.rename(columns={old: new}, inplace=True)
    return df


def load_hot_stock_rank(top_n: int) -> pd.DataFrame:
    """获取个股人气榜数据并截取前 top_n。"""
    df = fetch_with_retry(ak.stock_hot_rank_em, "个股人气榜")
    df = rename_columns(df, ['rank', 'code', 'name', 'heat', 'heat_change', 'heat_change_pct'])
    df.sort_values('rank', inplace=True)
    return df.head(top_n).reset_index(drop=True)


def load_stock_keywords(symbol: str) -> pd.DataFrame:
    """获取指定股票的热点题材关键词。"""
    df = fetch_with_retry(ak.stock_hot_keyword_em, f"{symbol} 热点题材", symbol)
    df = rename_columns(df, ['timestamp', 'code', 'concept_name', 'concept_code', 'concept_heat'])
    df['concept_heat'] = pd.to_numeric(df['concept_heat'], errors='coerce')
    df = df.dropna(subset=['concept_name', 'concept_code', 'concept_heat'])
    df = df.sort_values('timestamp').drop_duplicates(subset=['concept_code'], keep='last')
    return df.reset_index(drop=True)


@dataclass
class ConceptInfo:
    concept_code: str
    concept_name: str
    total_heat: float = 0.0
    members: List[Dict] = field(default_factory=list)


def build_hot_concepts(hot_df: pd.DataFrame, top_themes: int, members_per_theme: int) -> List[Dict]:
    """根据个股热词统计热点题材并挑选成员。"""
    concepts: Dict[str, ConceptInfo] = {}
    for _, row in hot_df.iterrows():
        symbol = row['code']
        try:
            kw_df = load_stock_keywords(symbol)
        except Exception as exc:  # pylint: disable=broad-except
            print(f"[WARN] 获取 {symbol} 热点题材失败: {exc}")
            continue
        for _, kw in kw_df.iterrows():
            cc = kw['concept_code']
            concept = concepts.setdefault(
                cc,
                ConceptInfo(
                    concept_code=cc,
                    concept_name=str(kw['concept_name']),
                    total_heat=0.0,
                ),
            )
            concept.total_heat += float(kw['concept_heat'])
            concept.members.append({
                'stock_code': symbol,
                'stock_name': row['name'],
                'concept_heat': float(kw['concept_heat']),
                'stock_hot_rank': int(row['rank']),
                'stock_hot_heat': float(row['heat']),
            })

    concept_list = sorted(concepts.values(), key=lambda c: c.total_heat, reverse=True)
    selected_concepts = concept_list[:top_themes]
    if not selected_concepts:
        return []

    selected_stocks = []
    seen_codes = set()
    for idx, concept in enumerate(selected_concepts, start=1):
        members = sorted(
            concept.members,
            key=lambda m: (m['concept_heat'], -m['stock_hot_rank']),
            reverse=True,
        )
        picks = []
        for pos, member in enumerate(members, start=1):
            if member['stock_code'] in seen_codes:
                continue
            picks.append({
                'theme_rank': idx,
                'theme_name': concept.concept_name,
                'theme_total_heat': concept.total_heat,
                'theme_member_rank': pos,
                'member_concept_heat': member['concept_heat'],
                **member,
            })
            seen_codes.add(member['stock_code'])
            if len(picks) >= members_per_theme:
                break
        selected_stocks.extend(picks)
    return selected_stocks


def load_daily_bars(symbol: str, lookback_days: int = 90) -> pd.DataFrame:
    """拉取指定股票的日线数据并截取最近 lookback_days。"""
    df = fetch_with_retry(ak.stock_zh_a_daily, f"{symbol} 日线", symbol)
    numeric_cols = ['open', 'high', 'low', 'close', 'volume', 'turnover']
    for col in numeric_cols:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors='coerce')
    df['date'] = pd.to_datetime(df['date'])
    df.sort_values('date', inplace=True)
    if lookback_days > 0:
        df = df.tail(lookback_days)
    df['ma5'] = df['close'].rolling(5).mean()
    df['ma10'] = df['close'].rolling(10).mean()
    return df.reset_index(drop=True)


def evaluate_stock(candidate: Dict) -> Dict:
    """根据策略规则评估单个股票。"""
    symbol = candidate['stock_code']
    norm_symbol = normalize_symbol(symbol)
    df = load_daily_bars(norm_symbol)

    if len(df) < 13:
        candidate['passed'] = False
        candidate['fail_reason'] = '历史数据不足 13 天'
        return candidate

    last_row = df.iloc[-1]
    prev_row = df.iloc[-2] if len(df) >= 2 else None

    volumes = df['volume'].dropna()
    if len(volumes) < 13:
        candidate['passed'] = False
        candidate['fail_reason'] = '成交量数据不足'
        return candidate
    vol_last3 = volumes.iloc[-3:].mean()
    vol_prev10 = volumes.iloc[-13:-3].mean()
    volume_ratio = vol_last3 / vol_prev10 if vol_prev10 > 0 else math.nan
    volume_pass = volume_ratio >= 1.5

    ma5 = last_row['ma5']
    ma10 = last_row['ma10']
    price_pass = last_row['close'] > ma5 and last_row['close'] > ma10

    turnover = last_row.get('turnover', math.nan)
    turnover_pass = turnover >= 0.05

    open_px = last_row['open']
    close_px = last_row['close']
    high_px = last_row['high']
    low_px = last_row['low']
    body = close_px - open_px
    total_range = high_px - low_px
    upper_shadow = high_px - max(close_px, open_px)
    kline_pass = (
        close_px > open_px
        and body > 0
        and total_range > 0
        and body / max(open_px, 1e-6) >= 0.005
        and body >= 0.5 * total_range
        and upper_shadow <= body
    )

    pct_change = None
    if prev_row is not None and prev_row['close'] not in (0, None):
        pct_change = (close_px - prev_row['close']) / prev_row['close'] * 100

    candidate.update({
        'trade_date': last_row['date'].date().isoformat(),
        'close': round(close_px, 3),
        'pct_change': round(pct_change, 2) if pct_change is not None else None,
        'turnover_pct': round(turnover * 100, 2) if pd.notna(turnover) else None,
        'volume_ratio': round(volume_ratio, 2) if not math.isnan(volume_ratio) else None,
        'ma5': round(ma5, 3) if pd.notna(ma5) else None,
        'ma10': round(ma10, 3) if pd.notna(ma10) else None,
        'rules': {
            'volume_expansion': volume_pass,
            'above_short_ma': price_pass,
            'turnover_ge_5pct': turnover_pass,
            'kline_strong_bull': kline_pass,
        },
    })
    if all(candidate['rules'].values()):
        candidate['passed'] = True
        candidate['fail_reason'] = ''
    else:
        failed = [name for name, ok in candidate['rules'].items() if not ok]
        candidate['passed'] = False
        candidate['fail_reason'] = ', '.join(failed)
    return candidate


def run_strategy(top_hot: int, top_themes: int, theme_members: int) -> List[Dict]:
    """执行策略并返回评估结果。"""
    proxy_backup = disable_global_proxy()
    try:
        hot_df = load_hot_stock_rank(top_hot)
        candidates = build_hot_concepts(hot_df, top_themes, theme_members)
        if not candidates:
            print("[INFO] 未找到满足热点题材约束的股票。")
            return []
        results = []
        for candidate in candidates:
            try:
                evaluated = evaluate_stock(candidate)
            except Exception as exc:  # pylint: disable=broad-except
                candidate['passed'] = False
                candidate['fail_reason'] = f'数据获取失败: {exc}'
                evaluated = candidate
            results.append(evaluated)
        return results
    finally:
        restore_global_proxy(proxy_backup)


def format_results(results: List[Dict]) -> None:
    """打印策略结果。"""
    if not results:
        print("没有可展示的结果。")
        return
    df = pd.DataFrame(results)
    display_cols = [
        'stock_code', 'stock_name', 'theme_name', 'theme_rank', 'theme_member_rank',
        'trade_date', 'close', 'pct_change', 'turnover_pct', 'volume_ratio',
        'passed', 'fail_reason'
    ]
    missing = [col for col in display_cols if col not in df.columns]
    for col in missing:
        df[col] = None
    df = df[display_cols].sort_values(['passed', 'theme_rank', 'theme_member_rank'], ascending=[False, True, True])
    print("\n=== 策略筛选结果 ===")
    print(df.to_string(index=False))
    winners = df[df['passed']]
    if not winners.empty:
        print("\n[PASS] 满足全部条件的标的：")
        for _, row in winners.iterrows():
            print(f"  - {row['stock_code']} {row['stock_name']} | 题材: {row['theme_name']} | 收盘: {row['close']} | 换手率: {row['turnover_pct']}% | 成交量放大量: x{row['volume_ratio']}")
    else:
        print("\n[INFO] 本次未找到满足全部条件的标的。")


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="热点题材成交量放大策略测试")
    parser.add_argument('--top-hot', type=int, default=60, help='参与统计的热点个股数量 (默认 60)')
    parser.add_argument('--themes', type=int, default=3, help='选择的热点题材数量 (默认 3)')
    parser.add_argument('--theme-members', type=int, default=3, help='每个题材挑选的个股数量 (默认 3)')
    return parser.parse_args(argv)


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    print("=============================================")
    print("热点题材成交量放大策略 - AKShare 实盘筛查模拟")
    print(f"运行时间：{datetime.now():%Y-%m-%d %H:%M:%S}")
    print(f"参数：top_hot={args.top_hot}, themes={args.themes}, theme_members={args.theme_members}")
    print("=============================================")
    try:
        results = run_strategy(args.top_hot, args.themes, args.theme_members)
        format_results(results)
        return 0
    except Exception as exc:  # pylint: disable=broad-except
        print(f"[ERROR] 策略执行失败: {exc}")
        return 1


if __name__ == '__main__':
    sys.exit(main())

