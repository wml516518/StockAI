import api from './api'

export const watchlistService = {
  // 获取所有自选股
  getWatchlist() {
    return api.get('/watchlist')
  },

  // 添加自选股
  addStock(stockCode, categoryId, costPrice, quantity) {
    return api.post('/watchlist/add', {
      stockCode,
      categoryId,
      costPrice,
      quantity
    })
  },

  // 删除自选股
  removeStock(id) {
    return api.delete(`/watchlist/${id}`)
  },

  // 更新自选股
  updateStock(id, costPrice, quantity) {
    return api.put(`/watchlist/${id}`, {
      costPrice,
      quantity
    })
  },

  // 获取所有分类
  getCategories() {
    return api.get('/watchlist/categories')
  },

  // 创建分类
  createCategory(name, description, color) {
    return api.post('/watchlist/categories', {
      name,
      description,
      color
    })
  },

  // 更新自选股分类
  updateCategory(id, categoryId) {
    return api.put(`/watchlist/${id}/category`, {
      categoryId
    })
  },

  // 更新建议价格
  updateSuggestedPrice(id, suggestedBuyPrice, suggestedSellPrice) {
    return api.put(`/watchlist/${id}/suggested-price`, {
      suggestedBuyPrice,
      suggestedSellPrice
    })
  },

  // 重置提醒标志
  resetAlertFlags(id, currentPrice) {
    return api.post(`/watchlist/${id}/reset-alerts`, {
      currentPrice
    })
  }
}

