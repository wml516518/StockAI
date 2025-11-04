import api from './api'

export const strategyOptimizationService = {
  // 优化策略
  optimizeStrategy(strategyId, stockCodes, startDate, endDate, optimizationConfig) {
    return api.post('/strategyoptimization/optimize', {
      strategyId,
      stockCodes,
      startDate,
      endDate,
      optimizationConfig
    })
  },

  // 获取优化历史
  getOptimizationHistory(strategyId) {
    return api.get(`/strategyoptimization/${strategyId}/history`)
  },

  // 应用最优参数
  applyOptimalParameters(strategyId, optimizationResultId) {
    return api.post(`/strategyoptimization/${strategyId}/apply/${optimizationResultId}`)
  },

  // 获取默认配置
  getDefaultConfig() {
    return api.get('/strategyoptimization/default-config')
  }
}

