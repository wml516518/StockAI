"""
技术指标计算模块
包含MACD、KDJ、均线、ATR等常用技术指标的计算
"""
import pandas as pd
import numpy as np


def calculate_macd(df, fast_period=12, slow_period=26, signal_period=9):
    """
    计算MACD指标
    
    Args:
        df: DataFrame，必须包含'close'列
        fast_period: 快速EMA周期，默认12
        slow_period: 慢速EMA周期，默认26
        signal_period: 信号线周期，默认9
    
    Returns:
        DataFrame with MACD_DIF, MACD_DEA, MACD_HIST columns
    """
    if df is None or df.empty or 'close' not in df.columns:
        return df
    
    df = df.copy()
    
    # 计算快速和慢速EMA
    ema_fast = df['close'].ewm(span=fast_period, adjust=False).mean()
    ema_slow = df['close'].ewm(span=slow_period, adjust=False).mean()
    
    # DIF = 快线 - 慢线
    df['macd_dif'] = ema_fast - ema_slow
    
    # DEA = DIF的EMA
    df['macd_dea'] = df['macd_dif'].ewm(span=signal_period, adjust=False).mean()
    
    # HIST = (DIF - DEA) * 2
    df['macd_hist'] = (df['macd_dif'] - df['macd_dea']) * 2
    
    return df


def calculate_kdj(df, n=9, m1=3, m2=3):
    """
    计算KDJ指标
    
    Args:
        df: DataFrame，必须包含'high', 'low', 'close'列
        n: RSV周期，默认9
        m1: K值平滑周期，默认3
        m2: D值平滑周期，默认3
    
    Returns:
        DataFrame with kdj_k, kdj_d, kdj_j columns
    """
    if df is None or df.empty:
        return df
    
    required_cols = ['high', 'low', 'close']
    if not all(col in df.columns for col in required_cols):
        return df
    
    df = df.copy()
    
    # 计算RSV (Raw Stochastic Value)
    low_n = df['low'].rolling(window=n, min_periods=1).min()
    high_n = df['high'].rolling(window=n, min_periods=1).max()
    
    rsv = (df['close'] - low_n) / (high_n - low_n) * 100
    rsv = rsv.fillna(50)  # 填充NaN为50
    
    # 计算K值 (K = SMA(RSV, m1))
    df['kdj_k'] = rsv.ewm(com=m1-1, adjust=False).mean()
    
    # 计算D值 (D = SMA(K, m2))
    df['kdj_d'] = df['kdj_k'].ewm(com=m2-1, adjust=False).mean()
    
    # 计算J值 (J = 3K - 2D)
    df['kdj_j'] = 3 * df['kdj_k'] - 2 * df['kdj_d']
    
    return df


def calculate_moving_averages(df, periods=[5, 10, 20, 60]):
    """
    计算移动平均线
    
    Args:
        df: DataFrame，必须包含'close'列
        periods: 均线周期列表，默认[5, 10, 20, 60]
    
    Returns:
        DataFrame with ma5, ma10, ma20, ma60 columns
    """
    if df is None or df.empty or 'close' not in df.columns:
        return df
    
    df = df.copy()
    
    for period in periods:
        col_name = f'ma{period}'
        df[col_name] = df['close'].rolling(window=period, min_periods=1).mean()
    
    return df


def calculate_atr(df, period=14):
    """
    计算ATR (Average True Range) 平均真实波幅
    
    Args:
        df: DataFrame，必须包含'high', 'low', 'close'列
        period: ATR周期，默认14
    
    Returns:
        DataFrame with atr column
    """
    if df is None or df.empty:
        return df
    
    required_cols = ['high', 'low', 'close']
    if not all(col in df.columns for col in required_cols):
        return df
    
    df = df.copy()
    
    # 计算True Range
    df['prev_close'] = df['close'].shift(1)
    
    df['tr1'] = df['high'] - df['low']
    df['tr2'] = abs(df['high'] - df['prev_close'])
    df['tr3'] = abs(df['low'] - df['prev_close'])
    
    df['tr'] = df[['tr1', 'tr2', 'tr3']].max(axis=1)
    
    # 计算ATR (TR的移动平均)
    df['atr'] = df['tr'].rolling(window=period, min_periods=1).mean()
    
    # 清理临时列
    df = df.drop(columns=['prev_close', 'tr1', 'tr2', 'tr3', 'tr'])
    
    return df


def calculate_volume_ma(df, period=5):
    """
    计算成交量移动平均
    
    Args:
        df: DataFrame，必须包含'volume'列
        period: 均量周期，默认5
    
    Returns:
        DataFrame with volume_ma column
    """
    if df is None or df.empty or 'volume' not in df.columns:
        return df
    
    df = df.copy()
    df[f'volume_ma{period}'] = df['volume'].rolling(window=period, min_periods=1).mean()
    
    return df


def calculate_all_indicators(df):
    """
    计算所有技术指标
    
    Args:
        df: DataFrame，包含OHLCV数据
    
    Returns:
        DataFrame with all technical indicators
    """
    if df is None or df.empty:
        return df
    
    # 标准化列名（转小写）
    df.columns = df.columns.str.lower()
    
    # 计算各项指标
    df = calculate_moving_averages(df)
    df = calculate_macd(df)
    df = calculate_kdj(df)
    df = calculate_atr(df)
    df = calculate_volume_ma(df, period=5)
    
    return df
