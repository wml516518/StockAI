import api from './api'

export const screenTemplateService = {
  // 获取所有模板
  getAll() {
    return api.get('/screentemplate')
  },

  // 根据ID获取模板
  getById(id) {
    return api.get(`/screentemplate/${id}`)
  },

  // 创建模板
  create(template) {
    return api.post('/screentemplate', template)
  },

  // 更新模板
  update(id, template) {
    return api.put(`/screentemplate/${id}`, template)
  },

  // 删除模板
  delete(id) {
    return api.delete(`/screentemplate/${id}`)
  },

  // 设置默认模板
  setDefault(id) {
    return api.post(`/screentemplate/${id}/set-default`)
  },

  // 从模板创建选股条件
  toCriteria(id) {
    return api.get(`/screentemplate/${id}/to-criteria`)
  }
}

