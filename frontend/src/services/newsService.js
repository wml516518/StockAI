import api from './api'

export const newsService = {
  // 获取新闻刷新设置
  async getRefreshSettings() {
    return await api.get('/news/refresh-settings')
  },

  // 更新新闻刷新设置
  async updateRefreshSettings(settings) {
    return await api.post('/news/refresh-settings', settings)
  },

  // 手动触发抓取新闻
  async fetchNews() {
    return await api.post('/news/fetch')
  }
}

