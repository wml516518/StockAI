import api from './api'

export const stockService = {
  // 获取股票实时行情
  getStock(code) {
    return api.get(`/stock/${code}`)
  },

  // 批量获取股票行情
  getBatchStocks(codes) {
    return api.post('/stock/batch', codes)
  },

  // 获取历史数据
  getHistory(code, startDate, endDate) {
    return api.get(`/stock/${code}/history`, {
      params: { startDate, endDate }
    })
  }
}

