import api from './api'

export const aiModelConfigService = {
  // 获取所有配置
  async getAll() {
    return await api.get('/aimodelconfig')
  },

  // 根据ID获取配置
  async getById(id) {
    return await api.get(`/aimodelconfig/${id}`)
  },

  // 创建配置
  async create(config) {
    return await api.post('/aimodelconfig', config)
  },

  // 更新配置
  async update(id, config) {
    return await api.put(`/aimodelconfig/${id}`, config)
  },

  // 删除配置
  async delete(id) {
    return await api.delete(`/aimodelconfig/${id}`)
  },

  // 测试连接
  async testConnection(request) {
    return await api.post('/aimodelconfig/test', request)
  }
}

