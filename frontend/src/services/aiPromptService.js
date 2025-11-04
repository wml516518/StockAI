import api from './api'

export const aiPromptService = {
  // 获取所有提示词
  async getAll() {
    return await api.get('/aiprompts')
  },

  // 获取默认提示词
  async getDefault() {
    return await api.get('/aiprompts/default')
  },

  // 根据ID获取提示词
  async getById(id) {
    return await api.get(`/aiprompts/${id}`)
  },

  // 创建提示词
  async create(prompt) {
    return await api.post('/aiprompts', prompt)
  },

  // 更新提示词
  async update(id, prompt) {
    return await api.put(`/aiprompts/${id}`, prompt)
  },

  // 删除提示词
  async delete(id) {
    return await api.delete(`/aiprompts/${id}`)
  }
}

