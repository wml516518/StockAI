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
  }
}


