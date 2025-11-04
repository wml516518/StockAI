import api from './api'

export const quantTradingService = {
  // 获取所有策略
  getAllStrategies() {
    return api.get('/quanttrading/strategies')
  },

  // 根据ID获取策略
  getStrategy(id) {
    return api.get(`/quanttrading/strategies/${id}`)
  },

  // 创建策略
  createStrategy(strategy) {
    return api.post('/quanttrading/strategies', strategy)
  },

  // 更新策略
  updateStrategy(id, strategy) {
    return api.put(`/quanttrading/strategies/${id}`, strategy)
  },

  // 删除策略
  deleteStrategy(id) {
    return api.delete(`/quanttrading/strategies/${id}`)
  },

  // 切换策略激活状态
  toggleStrategy(id) {
    return api.post(`/quanttrading/strategies/${id}/toggle`)
  },

  // 获取激活的策略
  getActiveStrategies() {
    return api.get('/quanttrading/strategies/active')
  },

  // 运行策略
  runStrategy(id, stockCodes) {
    return api.post(`/quanttrading/strategies/${id}/run`, { stockCodes })
  },

  // 获取策略信号
  getStrategySignals(id, startDate, endDate) {
    return api.get(`/quanttrading/strategies/${id}/signals`, {
      params: { startDate, endDate }
    })
  },

  // 获取策略交易记录
  getStrategyTrades(id, startDate, endDate) {
    return api.get(`/quanttrading/strategies/${id}/trades`, {
      params: { startDate, endDate }
    })
  }
}

