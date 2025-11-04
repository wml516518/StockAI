import api from './api'

export const strategyConfigService = {
  // 获取所有策略配置
  getAllConfigs() {
    return api.get('/strategyconfig/configs')
  },

  // 根据名称获取配置
  getConfigByName(name) {
    return api.get(`/strategyconfig/configs/${encodeURIComponent(name)}`)
  },

  // 从配置创建策略
  createStrategyFromConfig(configName) {
    return api.post(`/strategyconfig/create-from-config/${encodeURIComponent(configName)}`)
  },

  // 导入所有默认策略
  importStrategies() {
    return api.post('/strategyconfig/import')
  }
}

