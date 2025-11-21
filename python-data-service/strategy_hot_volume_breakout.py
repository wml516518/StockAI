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


def fetch_with_retry(func, description: str, *args, retries: int = 5, delay: float = 2.0, **kwargs):
    """统一的重试逻辑，针对连接错误使用指数退避策略。"""
    last_exc = None
    for attempt in range(1, retries + 1):
        try:
            data = func(*args, **kwargs)
            if data is not None:
                return data
        except Exception as exc:  # pylint: disable=broad-except
            last_exc = exc
            exc_str = str(exc)
            exc_type = type(exc).__name__
            
            # 判断是否为连接相关错误
            is_connection_error = any([
                'Connection' in exc_type,
                'Connection aborted' in exc_str,
                'ConnectionResetError' in exc_str,
                '10054' in exc_str,  # Windows 连接重置错误码
                '远程主机强迫关闭' in exc_str,
                'timeout' in exc_str.lower(),
                'time out' in exc_str.lower(),
            ])
            
            if is_connection_error:
                # 连接错误：使用指数退避，延迟时间更长
                base_delay = delay * 2  # 连接错误的基础延迟更长
                wait_time = base_delay * (2 ** (attempt - 1))  # 指数退避：4s, 8s, 16s, 32s...
                wait_time = min(wait_time, 30.0)  # 最多等待30秒
                if attempt < retries:
                    print(f"[WARN] {description} 连接错误 (尝试 {attempt}/{retries}): {exc_type} - 等待 {wait_time:.1f}秒后重试")
                else:
                    print(f"[WARN] {description} 连接错误 (尝试 {attempt}/{retries}): {exc_type} - 最后一次尝试")
            else:
                # 其他错误：使用线性延迟
                wait_time = delay * attempt
                if attempt < retries:
                    print(f"[WARN] {description} 失败 (尝试 {attempt}/{retries}): {exc_type} - {exc_str[:100]} - 等待 {wait_time:.1f}秒后重试")
                else:
                    print(f"[WARN] {description} 失败 (尝试 {attempt}/{retries}): {exc_type} - {exc_str[:100]} - 最后一次尝试")
            
            # 如果不是最后一次尝试，等待后继续重试
            if attempt < retries:
                time.sleep(wait_time)
    
    raise RuntimeError(f"无法获取 {description} (已重试 {retries} 次)") from last_exc


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
    total_rows = len(hot_df)
    for idx, (_, row) in enumerate(hot_df.iterrows(), 1):
        symbol = row['code']
        try:
            # 在请求之间添加延迟，避免请求过于频繁
            if idx > 1:
                time.sleep(0.3)  # 每个关键词请求之间延迟0.3秒
            
            kw_df = load_stock_keywords(symbol)
        except Exception as exc:  # pylint: disable=broad-except
            print(f"[WARN] 获取 {symbol} 热点题材失败 ({idx}/{total_rows}): {exc}")
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


def _normalize_history_dataframe(df: pd.DataFrame) -> pd.DataFrame:
    """标准化不同AKShare接口返回的数据格式。"""
    df = df.copy()
    
    # 标准化列名（支持中英文列名）
    column_mapping = {
        '日期': 'date',
        'Date': 'date',
        'date': 'date',
        '开盘': 'open',
        'Open': 'open',
        'open': 'open',
        '最高': 'high',
        'High': 'high',
        'high': 'high',
        '最低': 'low',
        'Low': 'low',
        'low': 'low',
        '收盘': 'close',
        'Close': 'close',
        'close': 'close',
        '成交量': 'volume',
        'Volume': 'volume',
        'volume': 'volume',
        '成交额': 'turnover',
        'Amount': 'turnover',
        'amount': 'turnover',
        '成交金额': 'turnover',
        'Turnover': 'turnover',
        'turnover': 'turnover',
    }
    
    # 重命名列
    for old_name, new_name in column_mapping.items():
        if old_name in df.columns and new_name not in df.columns:
            df.rename(columns={old_name: new_name}, inplace=True)
    
    # 确保必要的列存在（price相关列是必需的）
    required_price_cols = ['date', 'open', 'high', 'low', 'close']
    missing_price_cols = [col for col in required_price_cols if col not in df.columns]
    if missing_price_cols:
        raise ValueError(f"数据缺少必要列: {missing_price_cols}, 可用列: {list(df.columns)}")
    
    # 数值转换
    numeric_cols = ['open', 'high', 'low', 'close']
    if 'turnover' in df.columns:
        numeric_cols.append('turnover')
    if 'volume' in df.columns:
        numeric_cols.append('volume')
    
    for col in numeric_cols:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors='coerce')
    
    # 如果缺少 volume 列，尝试从 turnover 和 close 计算
    if 'volume' not in df.columns:
        if 'turnover' in df.columns and 'close' in df.columns:
            # 使用成交额和收盘价估算成交量：成交量 = 成交额 / 收盘价
            # 注意：这个估算可能不够精确，但对于策略分析来说可以使用
            df['volume'] = (df['turnover'] / df['close']).fillna(0)
            print(f"[INFO] 数据缺少 volume 列，已根据成交额和收盘价估算成交量")
        else:
            # 如果既没有 volume 也没有 turnover，设置为 0（会导致成交量相关策略失效）
            df['volume'] = 0
            print(f"[WARN] 数据缺少 volume 和 turnover 列，成交量将设为 0")
    
    # 日期转换
    df['date'] = pd.to_datetime(df['date'])
    
    # 排序
    df.sort_values('date', inplace=True)
    
    return df


def _try_fetch_volume_from_other_sources(symbol: str, target_dates: pd.Series, start_date_str: str, end_date_str: str) -> pd.Series:
    """尝试从其他数据源获取volume数据来补全缺失的列。
    
    返回一个Series，索引为日期，值为volume。如果无法获取，返回空Series。
    """
    # 尝试从 stock_zh_a_daily 获取 volume
    try:
        df_volume_raw = fetch_with_retry(
            ak.stock_zh_a_daily,
            f"{symbol} volume补全 (stock_zh_a_daily)",
            symbol,
            retries=2,  # 只重试2次，因为这是补全数据
            delay=1.5
        )
        if df_volume_raw is not None and not df_volume_raw.empty:
            # 标准化数据（会自动生成 volume 列，如果有 turnover/amount 的话）
            df_volume = _normalize_history_dataframe(df_volume_raw)
            # 只取目标日期范围内的数据
            if 'date' in df_volume.columns and 'volume' in df_volume.columns:
                df_volume = df_volume[df_volume['date'].isin(target_dates)]
                # 过滤掉无效的 volume 值（0、NaN）
                df_volume = df_volume[df_volume['volume'].notna() & (df_volume['volume'] > 0)]
                if len(df_volume) > 0:
                    return df_volume.set_index('date')['volume']
    except Exception as e:
        print(f"[DEBUG] {symbol} volume补全 (stock_zh_a_daily) 失败: {str(e)[:100]}")
        pass  # 静默失败，继续尝试其他方法
    
    # 尝试从 stock_zh_a_hist 获取 volume
    for adjust in ['', 'qfq', 'hfq']:
        try:
            def fetch_hist_volume():
                return ak.stock_zh_a_hist(
                    symbol=symbol,
                    period="daily",
                    start_date=start_date_str,
                    end_date=end_date_str,
                    adjust=adjust or ""
                )
            
            df_volume_raw = fetch_with_retry(
                fetch_hist_volume,
                f"{symbol} volume补全 (stock_zh_a_hist, {adjust})",
                retries=2,
                delay=1.5
            )
            if df_volume_raw is not None and not df_volume_raw.empty:
                # 标准化数据（会自动生成 volume 列，如果有 turnover/amount 的话）
                df_volume = _normalize_history_dataframe(df_volume_raw)
                if 'date' in df_volume.columns and 'volume' in df_volume.columns:
                    df_volume = df_volume[df_volume['date'].isin(target_dates)]
                    # 过滤掉无效的 volume 值（0、NaN）
                    df_volume = df_volume[df_volume['volume'].notna() & (df_volume['volume'] > 0)]
                    if len(df_volume) > 0:
                        return df_volume.set_index('date')['volume']
        except Exception as e:
            print(f"[DEBUG] {symbol} volume补全 (stock_zh_a_hist, {adjust}) 失败: {str(e)[:100]}")
            continue
    
    return pd.Series(dtype=float)


def load_daily_bars(symbol: str, lookback_days: int = 90) -> pd.DataFrame:
    """拉取指定股票的日线数据并截取最近 lookback_days。
    
    使用多个备用数据源，按顺序尝试：
    1. stock_zh_a_hist_tx - 优先使用（连接稳定性好）
    2. stock_zh_a_daily - 备用方案
    3. stock_zh_a_hist - 备用方案（前复权、无复权、后复权）
    
    如果使用 stock_zh_a_hist_tx 但缺少 volume 列，会尝试从其他接口补全。
    """
    from datetime import datetime, timedelta
    
    end_date = datetime.now()
    start_date = end_date - timedelta(days=lookback_days + 30)  # 多取一些数据确保足够
    start_date_str = start_date.strftime("%Y%m%d")
    end_date_str = end_date.strftime("%Y%m%d")
    
    # 方案1: 优先尝试 stock_zh_a_hist_tx（腾讯数据源，连接稳定性好）
    try:
        def fetch_hist_tx():
            return ak.stock_zh_a_hist_tx(
                symbol=symbol,
                start_date=start_date_str,
                end_date=end_date_str
            )
        
        df_raw = fetch_with_retry(
            fetch_hist_tx,
            f"{symbol} 日线 (stock_zh_a_hist_tx)",
            retries=3,
            delay=2.0
        )
        
        # 检查原始数据的列
        if df_raw is None or df_raw.empty:
            raise ValueError(f"{symbol} stock_zh_a_hist_tx 返回空数据")
        
        print(f"[DEBUG] {symbol} stock_zh_a_hist_tx 原始列: {list(df_raw.columns)}")
        
        # 检查是否有 volume 相关的列（支持中英文列名）
        has_volume = any(col in df_raw.columns for col in ['volume', 'Volume', '成交量'])
        has_amount_or_turnover = any(col in df_raw.columns for col in ['amount', 'Amount', 'turnover', 'Turnover', '成交额', '成交金额'])
        
        # stock_zh_a_hist_tx 返回的列：['date', 'open', 'close', 'high', 'low', 'amount']
        # amount 是成交额（金额），不是成交量（volume）
        if not has_volume and has_amount_or_turnover:
            print(f"[INFO] {symbol} 从 stock_zh_a_hist_tx 获取的数据缺少 volume 列（只有 amount 成交额），尝试从其他接口补全...")
            
            # 先标准化以获取日期列，用于匹配（此时会自动从 amount 和 close 估算 volume）
            df_temp = _normalize_history_dataframe(df_raw.copy())
            
            if 'date' in df_temp.columns and len(df_temp) > 0:
                target_dates = df_temp['date'].copy()
                
                # 尝试从其他接口获取真实的 volume 数据
                try:
                    volume_data = _try_fetch_volume_from_other_sources(symbol, target_dates, start_date_str, end_date_str)
                    
                    if len(volume_data) > 0 and not volume_data.isna().all():
                        # 成功从其他接口获取到 volume 数据，替换估算值
                        # 保存原始的估算值作为回退
                        estimated_volume = df_temp['volume'].copy() if 'volume' in df_temp.columns else None
                        # 使用真实的 volume 数据（如果日期匹配）
                        real_volume = df_temp['date'].map(volume_data)
                        # 对于没有匹配到的日期，使用估算值（已有）
                        if estimated_volume is not None:
                            df_temp['volume'] = real_volume.fillna(estimated_volume)
                        else:
                            df_temp['volume'] = real_volume.fillna(0)
                        df = df_temp
                        print(f"[INFO] {symbol} 成功从其他接口补全了 {len(volume_data)} 条真实 volume 数据")
                    else:
                        # 如果其他接口也失败，使用标准化函数自动估算（已在 _normalize_history_dataframe 中完成）
                        df = df_temp
                        print(f"[INFO] {symbol} 无法从其他接口获取真实 volume，已使用成交额(amount)和收盘价估算 volume")
                except Exception as e:
                    # 如果补全过程出错，使用已标准化的数据（已包含估算的 volume）
                    df = df_temp
                    print(f"[WARN] {symbol} 补全 volume 时出错: {str(e)[:100]}，已使用成交额和收盘价估算")
            else:
                # 如果标准化失败，直接标准化
                df = _normalize_history_dataframe(df_raw)
        elif has_volume:
            # 有 volume 列，直接标准化
            df = _normalize_history_dataframe(df_raw)
        else:
            # 既没有 volume 也没有 amount/turnover，直接标准化（会设置为 0）
            df = _normalize_history_dataframe(df_raw)
        if len(df) > 0:
            # 过滤日期范围
            df = df[df['date'] >= start_date]
            if len(df) > 0:
                if lookback_days > 0:
                    df = df.tail(lookback_days)
                df['ma5'] = df['close'].rolling(5).mean()
                df['ma10'] = df['close'].rolling(10).mean()
                print(f"[INFO] {symbol} 成功使用 stock_zh_a_hist_tx 获取日线数据")
                return df.reset_index(drop=True)
    except Exception as exc:
        exc_msg = str(exc)[:200]
        print(f"[WARN] {symbol} stock_zh_a_hist_tx 失败: {exc_msg}，尝试备用方案...")
    
    # 方案2: 尝试 stock_zh_a_daily（一次性获取全量数据）
    try:
        df = fetch_with_retry(
            ak.stock_zh_a_daily,
            f"{symbol} 日线 (stock_zh_a_daily)",
            symbol,
            retries=3,
            delay=2.5
        )
        df = _normalize_history_dataframe(df)
        if len(df) > 0:
            # 过滤日期范围
            df = df[df['date'] >= start_date]
            if len(df) > 0:
                if lookback_days > 0:
                    df = df.tail(lookback_days)
                df['ma5'] = df['close'].rolling(5).mean()
                df['ma10'] = df['close'].rolling(10).mean()
                print(f"[INFO] {symbol} 成功使用 stock_zh_a_daily 获取日线数据")
                return df.reset_index(drop=True)
    except Exception as exc:
        exc_msg = str(exc)[:200]
        print(f"[WARN] {symbol} stock_zh_a_daily 失败: {exc_msg}，尝试备用方案...")
    
    # 方案3: 尝试 stock_zh_a_hist（多个复权选项）
    adjust_options = ['qfq', '', 'hfq']  # 前复权、无复权、后复权
    for adjust in adjust_options:
        try:
            adjust_label = {'qfq': '前复权', 'hfq': '后复权', '': '无复权'}[adjust]
            
            def fetch_hist():
                return ak.stock_zh_a_hist(
                    symbol=symbol,
                    period="daily",
                    start_date=start_date_str,
                    end_date=end_date_str,
                    adjust=adjust or ""
                )
            
            df = fetch_with_retry(
                fetch_hist,
                f"{symbol} 日线 (stock_zh_a_hist, {adjust_label})",
                retries=3,
                delay=2.0
            )
            df = _normalize_history_dataframe(df)
            if len(df) > 0:
                if lookback_days > 0:
                    df = df.tail(lookback_days)
                df['ma5'] = df['close'].rolling(5).mean()
                df['ma10'] = df['close'].rolling(10).mean()
                print(f"[INFO] {symbol} 成功使用 stock_zh_a_hist ({adjust_label}) 获取日线数据")
                return df.reset_index(drop=True)
        except Exception as exc:
            exc_msg = str(exc)[:200]
            print(f"[WARN] {symbol} stock_zh_a_hist ({adjust}) 失败: {exc_msg}，继续尝试...")
            continue
    
    # 所有方案都失败
    raise RuntimeError(f"无法获取 {symbol} 的日线数据，所有备用方案都已尝试")


# 全局缓存实时行情数据，避免重复请求
_cached_spot_data = None
_cached_spot_time = None
_CACHE_DURATION = 60  # 缓存60秒

def get_turnover_rate(symbol: str) -> Optional[float]:
    """从实时行情API获取换手率（百分比，如5.0表示5%）。
    
    尝试多种方法：
    1. 从全市场实时行情数据中查找
    2. 从单个股票实时行情接口获取
    3. 如果都失败，返回None
    """
    global _cached_spot_data, _cached_spot_time
    
    # 方法1：从全市场实时行情数据中查找（使用缓存）
    try:
        current_time = time.time()
        if _cached_spot_data is None or _cached_spot_time is None or (current_time - _cached_spot_time) > _CACHE_DURATION:
            print(f"[DEBUG] 方法1：正在获取全市场实时行情数据...")
            try:
                _cached_spot_data = fetch_with_retry(
                    ak.stock_zh_a_spot_em,
                    "实时行情（换手率）",
                    retries=3,  # 增加重试次数
                    delay=2.0   # 增加延迟时间
                )
                _cached_spot_time = current_time
                if _cached_spot_data is not None and not _cached_spot_data.empty:
                    print(f"[DEBUG] 方法1：成功获取 {len(_cached_spot_data)} 条实时行情数据")
                    print(f"[DEBUG] 方法1：数据列名: {list(_cached_spot_data.columns)}")
                else:
                    print(f"[WARN] 方法1：获取的数据为空")
            except Exception as fetch_exc:
                # 网络错误时，不打印完整堆栈，只打印关键信息
                exc_msg = str(fetch_exc)
                if 'Connection' in exc_msg or 'timeout' in exc_msg.lower() or 'urllib3' in exc_msg:
                    print(f"[WARN] 方法1：网络连接失败，跳过（可能是网络问题或接口暂时不可用）")
                else:
                    print(f"[WARN] 方法1：获取数据失败: {exc_msg[:150]}")
                _cached_spot_data = None
        
        df = _cached_spot_data
        if df is not None and not df.empty:
            # 查找股票代码列（更宽松的匹配）
            code_col = None
            for col in df.columns:
                col_str = str(col)
                col_lower = col_str.lower()
                # 更宽松的匹配条件
                if ('代码' in col_str or 'code' in col_lower) and '名称' not in col_str and 'name' not in col_lower:
                    code_col = col
                    break
            
            if code_col is None:
                print(f"[DEBUG] 方法1：未找到代码列，所有列: {list(df.columns)}")
            else:
                symbol_clean = symbol.replace('sh', '').replace('sz', '').replace('SH', '').replace('SZ', '').strip()
                print(f"[DEBUG] 方法1：查找股票代码 {symbol_clean}，代码列: {code_col}")
                
                # 查找换手率列（更宽松的匹配）
                turnover_col = None
                for col in df.columns:
                    col_str = str(col)
                    col_lower = col_str.lower()
                    # 更宽松的匹配：换手、turnover、rate等关键词
                    if ('换手' in col_str or 
                        ('turnover' in col_lower and 'rate' in col_lower) or
                        (col_lower == 'turnoverrate') or
                        ('turnover' in col_lower and 'ratio' in col_lower)):
                        turnover_col = col
                        break
                
                if turnover_col is None:
                    print(f"[DEBUG] 方法1：未找到换手率列，所有列: {list(df.columns)}")
                else:
                    print(f"[DEBUG] 方法1：找到换手率列: {turnover_col}")
                    # 尝试匹配股票代码
                    found = False
                    for idx, row in df.iterrows():
                        code_val = str(row[code_col]).replace('sh', '').replace('sz', '').replace('SH', '').replace('SZ', '').strip()
                        if code_val == symbol_clean:
                            found = True
                            turnover_rate = row[turnover_col]
                            print(f"[DEBUG] 方法1：找到匹配股票，换手率原始值: {turnover_rate} (类型: {type(turnover_rate)})")
                            if pd.notna(turnover_rate):
                                try:
                                    # 尝试直接转换
                                    rate_value = float(turnover_rate)
                                    if 0 <= rate_value <= 1000:
                                        print(f"[INFO] 方法1成功获取 {symbol} 换手率: {rate_value}%")
                                        return rate_value
                                    else:
                                        print(f"[DEBUG] 方法1：换手率值超出合理范围: {rate_value}")
                                except (ValueError, TypeError):
                                    # 尝试移除百分号
                                    try:
                                        value_str = str(turnover_rate).replace('%', '').strip()
                                        rate_value = float(value_str)
                                        if 0 <= rate_value <= 1000:
                                            print(f"[INFO] 方法1成功获取 {symbol} 换手率: {rate_value}%")
                                            return rate_value
                                    except (ValueError, TypeError) as e:
                                        print(f"[DEBUG] 方法1解析 {symbol} 换手率值失败: {turnover_rate}, 错误: {e}")
                    
                    if not found:
                        # 打印前几条数据用于调试
                        print(f"[DEBUG] 方法1未找到匹配的股票代码: {symbol_clean}")
                        if len(df) > 0:
                            print(f"[DEBUG] 方法1：前3条数据的代码列值: {df[code_col].head(3).tolist()}")
    except Exception as exc:
        exc_msg = str(exc)
        # 对于网络错误，不打印完整堆栈
        if 'Connection' in exc_msg or 'timeout' in exc_msg.lower() or 'urllib3' in exc_msg:
            print(f"[WARN] 方法1获取 {symbol} 换手率失败: 网络连接问题")
        else:
            print(f"[DEBUG] 方法1获取 {symbol} 换手率失败: {exc_msg[:200]}")
    
    # 方法2：使用单个股票的实时行情接口
    try:
        # 尝试使用 stock_individual_info_em 获取单个股票信息
        symbol_clean = symbol.replace('sh', '').replace('sz', '').replace('SH', '').replace('SZ', '').strip()
        # 构造完整的股票代码（带市场前缀）
        if symbol.startswith('sh') or symbol.startswith('SH'):
            full_code = f"sh{symbol_clean}"
        elif symbol.startswith('sz') or symbol.startswith('SZ'):
            full_code = f"sz{symbol_clean}"
        else:
            # 根据代码判断市场
            full_code = f"sh{symbol_clean}" if symbol_clean.startswith('6') else f"sz{symbol_clean}"
        
        # 尝试不同的参数名
        df_info = None
        try:
            df_info = fetch_with_retry(
                lambda: ak.stock_individual_info_em(symbol=full_code),
                f"{symbol} 个股信息（方法2a）",
                retries=2,
                delay=1.0
            )
        except Exception as e:
            exc_msg = str(e)
            if 'Connection' not in exc_msg and 'timeout' not in exc_msg.lower() and 'urllib3' not in exc_msg:
                # 只有非网络错误才尝试其他方法
                try:
                    df_info = fetch_with_retry(
                        lambda: ak.stock_individual_info_em(stock=full_code),
                        f"{symbol} 个股信息（方法2b）",
                        retries=2,
                        delay=1.0
                    )
                except Exception:
                    try:
                        # 尝试不带前缀的代码
                        df_info = fetch_with_retry(
                            lambda: ak.stock_individual_info_em(symbol=symbol_clean),
                            f"{symbol} 个股信息（方法2c）",
                            retries=2,
                            delay=1.0
                        )
                    except Exception:
                        pass
            else:
                print(f"[WARN] 方法2：网络连接失败，跳过")
        
        if df_info is not None and not df_info.empty:
            # 查找换手率字段
            for idx, row in df_info.iterrows():
                # 尝试不同的行格式
                if len(row) >= 2:
                    item_name = str(row.iloc[0]) if pd.notna(row.iloc[0]) else ''
                    item_value = row.iloc[1] if len(row) > 1 else None
                elif len(row) == 1:
                    # 可能是单列格式，尝试从列名或值中提取
                    item_name = str(row.index[0]) if len(row.index) > 0 else ''
                    item_value = row.iloc[0] if len(row) > 0 else None
                else:
                    continue
                
                if '换手' in item_name and item_value is not None:
                    try:
                        # 移除百分号并转换为浮点数
                        value_str = str(item_value).replace('%', '').strip()
                        rate_value = float(value_str)
                        if 0 <= rate_value <= 1000:
                            print(f"[INFO] 方法2成功获取 {symbol} 换手率: {rate_value}%")
                            return rate_value
                    except (ValueError, TypeError) as e:
                        print(f"[DEBUG] 方法2解析 {symbol} 换手率值失败: {item_value}, 错误: {e}")
    except Exception as exc:
        exc_msg = str(exc)
        if 'Connection' in exc_msg or 'timeout' in exc_msg.lower() or 'urllib3' in exc_msg:
            print(f"[WARN] 方法2获取 {symbol} 换手率失败: 网络连接问题")
        else:
            print(f"[DEBUG] 方法2获取 {symbol} 换手率失败: {exc_msg[:150]}")
    
    # 方法3：尝试使用 stock_zh_a_spot 接口（另一个实时行情接口）
    try:
        symbol_clean = symbol.replace('sh', '').replace('sz', '').replace('SH', '').replace('SZ', '').strip()
        # 构造完整的股票代码
        if symbol.startswith('sh') or symbol.startswith('SH'):
            full_code = f"sh{symbol_clean}"
        elif symbol.startswith('sz') or symbol.startswith('SZ'):
            full_code = f"sz{symbol_clean}"
        else:
            full_code = f"sh{symbol_clean}" if symbol_clean.startswith('6') else f"sz{symbol_clean}"
        
        # 尝试使用 stock_zh_a_spot 获取单个股票实时行情
        try:
            df_spot = fetch_with_retry(
                lambda: ak.stock_zh_a_spot(symbol=full_code),
                f"{symbol} 实时行情（方法3）",
                retries=2,
                delay=1.0
            )
            if df_spot is not None and not df_spot.empty:
                print(f"[DEBUG] 方法3：获取到数据，列名: {list(df_spot.columns)}")
                # 查找换手率字段
                for col in df_spot.columns:
                    col_str = str(col).lower()
                    if '换手' in str(col) or ('turnover' in col_str and 'rate' in col_str):
                        turnover_rate = df_spot.iloc[0][col]
                        if pd.notna(turnover_rate):
                            try:
                                rate_value = float(str(turnover_rate).replace('%', '').strip())
                                if 0 <= rate_value <= 1000:
                                    print(f"[INFO] 方法3成功获取 {symbol} 换手率: {rate_value}%")
                                    return rate_value
                            except (ValueError, TypeError):
                                pass
        except Exception as e:
            exc_msg = str(e)
            if 'Connection' in exc_msg or 'timeout' in exc_msg.lower() or 'urllib3' in exc_msg:
                print(f"[WARN] 方法3：网络连接失败，跳过")
            else:
                print(f"[DEBUG] 方法3尝试失败: {exc_msg[:150]}")
    except Exception as exc:
        exc_msg = str(exc)
        if 'Connection' in exc_msg or 'timeout' in exc_msg.lower() or 'urllib3' in exc_msg:
            print(f"[WARN] 方法3获取 {symbol} 换手率失败: 网络连接问题")
        else:
            print(f"[DEBUG] 方法3获取 {symbol} 换手率失败: {exc_msg[:150]}")
    
    # 所有方法都失败
    print(f"[WARN] 所有方法都无法获取 {symbol} 的换手率")
    return None


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

    # 从实时行情API获取换手率（百分比，如5.0表示5%）
    # 注意：不能使用成交额(turnover)作为换手率，这是错误的！
    turnover_rate_pct = get_turnover_rate(norm_symbol)
    if turnover_rate_pct is None:
        # 如果无法获取换手率，打印警告信息
        print(f"[WARN] {symbol} 无法获取换手率，换手率条件将不通过")
        turnover_rate_pct = None
        turnover_pass = False
    else:
        # 换手率已经是百分比形式（如5.0表示5%），判断是否>=5%
        turnover_pass = turnover_rate_pct >= 5.0
        print(f"[INFO] {symbol} 换手率: {turnover_rate_pct:.2f}%")

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
        'turnover_pct': round(turnover_rate_pct, 2) if turnover_rate_pct is not None else None,
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
        total = len(candidates)
        for idx, candidate in enumerate(candidates, 1):
            try:
                symbol = candidate.get('stock_code', 'unknown')
                print(f"[INFO] 正在评估 ({idx}/{total}): {symbol}")
                
                # 在评估股票前添加短暂延迟，避免请求过于频繁
                if idx > 1:
                    time.sleep(0.5)  # 每个股票之间延迟0.5秒
                
                evaluated = evaluate_stock(candidate)
            except Exception as exc:  # pylint: disable=broad-except
                symbol = candidate.get('stock_code', 'unknown')
                exc_msg = str(exc)[:200]  # 截断过长的错误信息
                print(f"[WARN] 评估 {symbol} 失败: {exc_msg}")
                candidate['passed'] = False
                candidate['fail_reason'] = f'数据获取失败: {exc_msg}'
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

