import api from './api'

export const alertService = {
  // 创建价格提醒
  createAlert(stockCode, targetPrice, type) {
    return api.post('/alert/create', {
      stockCode,
      targetPrice,
      type
    })
  },

  // 获取所有活跃提醒
  getActiveAlerts() {
    return api.get('/alert/active')
  },

  // 删除提醒
  deleteAlert(id) {
    return api.delete(`/alert/${id}`)
  },

  // 检查提醒
  checkAlerts() {
    return api.post('/alert/check')
  }
}

