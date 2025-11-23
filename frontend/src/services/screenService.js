import api from './api'

export const screenService = {
  fetchShortTermHotStrategy(params = {}) {
    const { topHot = 60, topThemes = 3, themeMembers = 3 } = params
    return api.get('/screen/short-term/hot-volume-breakout', {
      params: {
        topHot,
        topThemes,
        themeMembers
      },
      timeout: 300000 // 5分钟，策略调用可能较慢
    })
  },

  /**
   * AI选股：使用自然语言描述选股条件
   * @param {string} naturalLanguage - 自然语言描述的选股条件
   * @param {number} pageIndex - 页码（可选）
   * @param {number} pageSize - 每页数量（可选）
   * @param {number} modelId - AI模型ID（可选）
   */
  aiSearch(naturalLanguage, pageIndex = 1, pageSize = 10, modelId = null) {
    const requestData = {
      naturalLanguage: String(naturalLanguage || '').trim(),
      pageIndex: Number(pageIndex) || 1,
      pageSize: Number(pageSize) || 10
    }
    if (modelId) {
      requestData.modelId = Number(modelId)
    }
    
    console.log('AI选股请求数据:', JSON.stringify(requestData, null, 2))
    
    return api.post('/screen/ai-search', requestData, {
      timeout: 300000, // 5分钟，AI解析和选股可能需要较长时间
      headers: {
        'Content-Type': 'application/json'
      }
    })
  }
}


