<template>
  <div class="container">
    <div class="content">
      <!-- 创建价格提醒 -->
      <div class="card">
        <h3>创建价格提醒</h3>
        <div class="form-group">
          <label>股票代码</label>
          <input v-model="form.stockCode" type="text" placeholder="输入股票代码">
        </div>
        <div class="form-group">
          <label>目标价格</label>
          <input v-model.number="form.targetPrice" type="number" step="0.01" placeholder="输入目标价格">
        </div>
        <div class="form-group">
          <label>提醒类型</label>
          <select v-model="form.type">
            <option value="PriceUp">价格涨到目标价</option>
            <option value="PriceDown">价格跌到目标价</option>
            <option value="PriceReach">价格到达目标价</option>
          </select>
        </div>
        <button class="btn" @click="handleCreateAlert" :disabled="loading">创建提醒</button>
      </div>

      <!-- 活跃提醒列表 -->
      <div class="card">
        <h3>活跃提醒</h3>
        <div v-if="loading" class="loading">加载中...</div>
        <div v-else-if="alerts.length === 0" class="loading">暂无提醒</div>
        <div v-else class="alerts-list">
          <div v-for="alert in alerts" :key="alert.id" class="alert-item">
            <div class="alert-info">
              <div class="alert-stock">{{ alert.stockCode }}</div>
              <div class="alert-details">
                <span>目标价: {{ alert.targetPrice.toFixed(2) }}</span>
                <span>类型: {{ getAlertTypeText(alert.type) }}</span>
                <span v-if="alert.createdAt" class="alert-time">
                  {{ formatDate(alert.createdAt) }}
                </span>
              </div>
            </div>
            <button class="btn btn-danger btn-small" @click="handleDeleteAlert(alert.id)">删除</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import { alertService } from '../services/alertService'

const alerts = ref([])
const loading = ref(false)

const form = ref({
  stockCode: '',
  targetPrice: null,
  type: 'PriceUp'
})

const fetchAlerts = async () => {
  loading.value = true
  try {
    alerts.value = await alertService.getActiveAlerts()
  } catch (error) {
    console.error('获取提醒失败:', error)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await fetchAlerts()
})

// 组件激活时重新加载数据
onActivated(async () => {
  await fetchAlerts()
})

const handleCreateAlert = async () => {
  if (!form.value.stockCode || !form.value.targetPrice) {
    alert('请填写完整信息')
    return
  }
  try {
    await alertService.createAlert(
      form.value.stockCode,
      form.value.targetPrice,
      form.value.type
    )
    form.value = { stockCode: '', targetPrice: null, type: 'PriceUp' }
    await fetchAlerts()
  } catch (error) {
    alert('创建失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleDeleteAlert = async (id) => {
  if (!confirm('确定要删除这个提醒吗？')) return
  try {
    await alertService.deleteAlert(id)
    await fetchAlerts()
  } catch (error) {
    alert('删除失败: ' + (error.response?.data?.message || error.message))
  }
}

const getAlertTypeText = (type) => {
  const map = {
    PriceUp: '价格涨到目标价',
    PriceDown: '价格跌到目标价',
    PriceReach: '价格到达目标价'
  }
  return map[type] || type
}

const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN')
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.alerts-list {
  margin-top: 20px;
}

.alert-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 15px;
  border: 1px solid #e0e0e0;
  border-radius: 5px;
  margin-bottom: 10px;
  background: #f8f9fa;
}

.alert-info {
  flex: 1;
}

.alert-stock {
  font-size: 1.2em;
  font-weight: bold;
  color: #333;
  margin-bottom: 5px;
}

.alert-details {
  display: flex;
  gap: 15px;
  font-size: 0.9em;
  color: #666;
}

.alert-time {
  color: #999;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .alert-item {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }
  
  .alert-details {
    flex-direction: column;
    gap: 5px;
  }
}
</style>

