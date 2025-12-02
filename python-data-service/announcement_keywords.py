"""
公告关键词检测模块
用于识别负面公告和风险信号
"""

# 负面关键词列表
NEGATIVE_KEYWORDS = {
    # 业绩相关
    '业绩下滑', '业绩预亏', '亏损预告', '亏损', '净利润下降', '营收下降',
    '业绩大幅下滑', '业绩变脸', '业绩爆雷',
    
    # 高管相关
    '高管减持', '董事减持', '监事减持', '高管离职', '董事长辞职',
    '总经理辞职', '财务总监辞职',
    
    # 股权相关
    '大额解禁', '限售股解禁', '股权质押', '股权冻结',
    
    # 监管相关
    '监管处罚', '立案调查', '证监会调查', '交易所问询', '监管函',
    '违规', '处罚', '立案', '调查',
    
    # 评级相关
    '评级下调', '下调评级', '下调目标价', '卖出评级',
    
    # 风险相关
    '风险提示', '重大风险', '诉讼', '仲裁', '债务违约',
    '资金链', '流动性风险', '退市风险',
    
    # 其他负面
    '商誉减值', '资产减值', '计提', '坏账', '存货跌价'
}

def detect_negative_keywords(text):
    """
    检测文本中的负面关键词
    
    Args:
        text: 要检测的文本（标题或内容）
    
    Returns:
        tuple: (is_negative, matched_keywords)
            - is_negative: 是否包含负面关键词
            - matched_keywords: 匹配到的关键词列表
    """
    if not text:
        return False, []
    
    text = str(text).lower()
    matched = []
    
    for keyword in NEGATIVE_KEYWORDS:
        if keyword in text:
            matched.append(keyword)
    
    return len(matched) > 0, matched
