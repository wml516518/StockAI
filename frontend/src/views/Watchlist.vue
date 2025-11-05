<template>
  <div class="container">
    <div class="content">
      <!-- 添加自选股表单 -->
      <div class="card">
        <h3>添加自选股</h3>
        <div class="form-group">
          <label>股票代码（如：000001）</label>
          <input v-model="form.stockCode" type="text" placeholder="输入股票代码">
        </div>
        <div class="form-group">
          <label>分类</label>
          <div style="display: flex; gap: 10px;">
            <select v-model="form.categoryId" style="flex: 1;">
              <option value="">选择分类...</option>
              <option v-for="cat in categories" :key="cat.id" :value="cat.id">
                {{ cat.name }}
              </option>
            </select>
            <button class="btn" @click="showCreateCategory = true">+ 新建分类</button>
          </div>
        </div>
        <div class="form-group">
          <label>成本价（可选）</label>
          <input v-model.number="form.costPrice" type="number" step="0.01" placeholder="输入成本价">
        </div>
        <div class="form-group">
          <label>持仓数量（可选）</label>
          <input v-model.number="form.quantity" type="number" placeholder="输入持仓数量">
        </div>
        <button class="btn" @click="handleAddStock" :disabled="loading">添加到自选股</button>
      </div>

      <!-- 创建分类对话框 -->
      <div v-if="showCreateCategory" class="modal" @click.self="showCreateCategory = false">
        <div class="modal-content">
          <div class="modal-header">
            <h3>创建新分类</h3>
            <span class="close" @click="showCreateCategory = false">&times;</span>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>分类名称 *</label>
              <input v-model="categoryForm.name" type="text" placeholder="如：已购、预购、关注">
            </div>
            <div class="form-group">
              <label>描述</label>
              <input v-model="categoryForm.description" type="text" placeholder="分类描述（可选）">
            </div>
            <div class="form-group">
              <label>颜色</label>
              <input v-model="categoryForm.color" type="color" value="#1890ff">
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn" @click="handleCreateCategory">创建</button>
            <button class="btn btn-secondary" @click="showCreateCategory = false">取消</button>
          </div>
        </div>
      </div>

      <!-- 自选股列表 -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 style="margin: 0;">我的自选股</h3>
            <p class="refresh-info">
              自动刷新: <span>{{ autoRefreshEnabled ? '已启用' : '已暂停' }}</span> | 
              间隔: <span>{{ refreshInterval }}秒</span>
            </p>
          </div>
          <button class="btn" @click="toggleAutoRefresh">
            {{ autoRefreshEnabled ? '⏸️ 暂停' : '▶️ 开始' }}
          </button>
        </div>
        <div v-if="loading" class="loading">加载中...</div>
        <div v-else-if="stocks.length === 0" class="loading">暂无自选股</div>
        <div v-else class="stock-cards">
          <div v-for="(categoryStocks, categoryName) in stocksByCategory" :key="categoryName" class="category-group">
            <h4 class="category-title" :style="{ color: getCategoryColor(categoryName) }">
              {{ categoryName }}
            </h4>
            <div class="stock-grid">
              <div v-for="stock in categoryStocks" :key="stock.id" class="stock-card">
                <div class="stock-header">
                  <div class="stock-name-section">
                    <div class="stock-name">{{ stock.stock?.name || stock.stockName || stock.stockCode }}</div>
                    <div class="stock-code">{{ stock.stockCode }}</div>
                  </div>
                  <div class="stock-actions">
                    <button class="btn btn-small btn-info" @click="handleAIAnalyze(stock.stockCode)" title="AI分析">🤖 AI分析</button>
                    <button class="btn btn-small btn-danger" @click="handleRemoveStock(stock.id)">删除</button>
                  </div>
                </div>
                <div class="price-section">
                  <div class="current-price" :class="getPriceClass(getStockChangePercent(stock))">
                    {{ formatPrice(getStockPrice(stock)) }}
                  </div>
                  <div class="price-info-row">
                    <div class="price-item">
                      <span class="price-label">涨跌幅</span>
                      <span class="price-value" :class="getPriceClass(getStockChangePercent(stock))">
                        {{ formatPercent(getStockChangePercent(stock)) }}
                      </span>
                    </div>
                    <div class="price-item">
                      <span class="price-label">涨跌额</span>
                      <span class="price-value" :class="getPriceClass(getStockChange(stock))">
                        {{ formatPrice(getStockChange(stock)) }}
                      </span>
                    </div>
                  </div>
                  <div class="price-info-row">
                    <div class="price-item">
                      <span class="price-label">最高</span>
                      <span class="price-value">{{ formatPrice(getStockHigh(stock)) }}</span>
                    </div>
                    <div class="price-item">
                      <span class="price-label">最低</span>
                      <span class="price-value">{{ formatPrice(getStockLow(stock)) }}</span>
                    </div>
                  </div>
                </div>
                <div class="cost-info" :class="stock.costPrice ? getCostClass(stock) : 'cost-neutral'">
                  <div v-if="stock.costPrice">
                    <div>成本: {{ formatPrice(stock.costPrice) }} × {{ stock.quantity || 0 }}</div>
                    <div>盈亏: {{ formatPrice(calculateProfit(stock)) }} ({{ formatPercent(calculateProfitPercent(stock)) }})</div>
                  </div>
                  <div v-else>
                    未设置成本价
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, onActivated, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useWatchlistStore } from '../stores/watchlist'
import api from '../services/api'

const watchlistStore = useWatchlistStore()
const route = useRoute()
const router = useRouter()
const stocks = computed(() => watchlistStore.stocks)
const categories = computed(() => watchlistStore.categories)
const loading = computed(() => watchlistStore.loading)
const autoRefreshEnabled = computed({
  get: () => watchlistStore.autoRefreshEnabled,
  set: (value) => { watchlistStore.autoRefreshEnabled = value }
})
const refreshInterval = computed(() => watchlistStore.refreshInterval)
const stocksByCategory = computed(() => watchlistStore.stocksByCategory)

const form = ref({
  stockCode: '',
  categoryId: '',
  costPrice: null,
  quantity: null
})

const categoryForm = ref({
  name: '',
  description: '',
  color: '#1890ff'
})

const showCreateCategory = ref(false)
let refreshTimer = null

// 组件挂载时加载数据
onMounted(async () => {
  // 从localStorage加载设置
  loadSettings()
  await watchlistStore.fetchWatchlist()
  await watchlistStore.fetchCategories()
  startAutoRefresh()
  
  // 监听store中的refreshInterval变化，重新创建定时器
  watch(() => watchlistStore.refreshInterval, (newInterval) => {
    if (autoRefreshEnabled.value) {
      startAutoRefresh()
    }
  })
  
  // 监听store中的autoRefreshEnabled变化
  watch(() => watchlistStore.autoRefreshEnabled, (enabled) => {
    if (enabled) {
      startAutoRefresh()
    } else {
      stopAutoRefresh()
    }
  })
})

// 组件激活时恢复自动刷新（用于路由切换回来时，keep-alive 会触发此钩子）
onActivated(() => {
  // 重新加载设置，确保使用最新的刷新间隔
  loadSettings()
  // 只恢复自动刷新，不重新获取数据
  startAutoRefresh()
})

onUnmounted(() => {
  stopAutoRefresh()
})

// 加载设置
const loadSettings = () => {
  const savedInterval = localStorage.getItem('refreshInterval')
  const savedEnabled = localStorage.getItem('autoRefreshEnabled')
  
  if (savedInterval) {
    const interval = parseFloat(savedInterval)
    watchlistStore.refreshInterval = interval
    refreshInterval.value = interval
  } else {
    refreshInterval.value = watchlistStore.refreshInterval
  }
  
  if (savedEnabled !== null) {
    const enabled = savedEnabled === 'true'
    watchlistStore.autoRefreshEnabled = enabled
    autoRefreshEnabled.value = enabled
  } else {
    autoRefreshEnabled.value = watchlistStore.autoRefreshEnabled
  }
}

const startAutoRefresh = () => {
  if (refreshTimer) clearInterval(refreshTimer)
  if (autoRefreshEnabled.value) {
    const intervalSeconds = refreshInterval.value || watchlistStore.refreshInterval || 3
    refreshTimer = setInterval(() => {
      watchlistStore.refreshPrices()
    }, intervalSeconds * 1000)
  }
}

const stopAutoRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
}

const toggleAutoRefresh = () => {
  autoRefreshEnabled.value = !autoRefreshEnabled.value
  watchlistStore.autoRefreshEnabled = autoRefreshEnabled.value
  localStorage.setItem('autoRefreshEnabled', autoRefreshEnabled.value.toString())
  if (autoRefreshEnabled.value) {
    startAutoRefresh()
  } else {
    stopAutoRefresh()
  }
}

const handleAddStock = async () => {
  if (!form.value.stockCode) {
    alert('请输入股票代码')
    return
  }
  try {
    await watchlistStore.addStock(
      form.value.stockCode,
      form.value.categoryId || null,
      form.value.costPrice || null,
      form.value.quantity || null
    )
    form.value = { stockCode: '', categoryId: '', costPrice: null, quantity: null }
  } catch (error) {
    alert('添加失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleRemoveStock = async (id) => {
  if (!confirm('确定要删除这只股票吗？')) return
  try {
    await watchlistStore.removeStock(id)
  } catch (error) {
    alert('删除失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleCreateCategory = async () => {
  if (!categoryForm.value.name) {
    alert('请输入分类名称')
    return
  }
  try {
    await watchlistStore.createCategory(
      categoryForm.value.name,
      categoryForm.value.description,
      categoryForm.value.color
    )
    categoryForm.value = { name: '', description: '', color: '#1890ff' }
    showCreateCategory.value = false
  } catch (error) {
    alert('创建失败: ' + (error.response?.data?.message || error.message))
  }
}

const getCategoryColor = (categoryName) => {
  const category = categories.value.find(c => c.name === categoryName)
  return category?.color || '#667eea'
}

const getPriceClass = (value) => {
  if (!value) return ''
  return value > 0 ? 'price-up' : value < 0 ? 'price-down' : ''
}

const getCostClass = (stock) => {
  const profit = calculateProfit(stock)
  return profit >= 0 ? 'cost-positive' : 'cost-negative'
}

const calculateProfit = (stock) => {
  const currentPrice = getStockPrice(stock)
  if (!stock.costPrice || !stock.quantity || !currentPrice) return 0
  return (currentPrice - stock.costPrice) * stock.quantity
}

const calculateProfitPercent = (stock) => {
  const currentPrice = getStockPrice(stock)
  if (!stock.costPrice || !currentPrice) return 0
  return ((currentPrice - stock.costPrice) / stock.costPrice) * 100
}

const formatPrice = (price) => {
  if (price === null || price === undefined) return '-'
  return price.toFixed(2)
}

const formatPercent = (percent) => {
  if (percent === null || percent === undefined) return '-'
  return (percent > 0 ? '+' : '') + percent.toFixed(2) + '%'
}

// AI分析
const handleAIAnalyze = (stockCode) => {
  // 跳转到AI分析页面，并传递股票代码
  router.push({ path: '/ai', query: { stockCode } })
}

// 获取股票价格相关的辅助函数
const getStockPrice = (stock) => {
  return stock.stock?.currentPrice || stock.stock?.price || stock.currentPrice || 0
}

const getStockChange = (stock) => {
  return stock.stock?.change || stock.change || 0
}

const getStockChangePercent = (stock) => {
  return stock.stock?.changePercent || stock.changePercent || 0
}

const getStockHigh = (stock) => {
  // 优先使用 highPrice（后端返回的 JSON 字段名），然后尝试其他可能的字段名
  const high = stock.stock?.highPrice || stock.stock?.high || stock.highPrice || stock.high || 0
  // 如果最高价为0，使用当前价作为回退（非交易时间可能为0）
  const currentPrice = getStockPrice(stock)
  if (high > 0) {
    return high
  }
  // 如果最高价为0但有当前价，使用当前价
  if (currentPrice > 0) {
    return currentPrice
  }
  return 0
}

const getStockLow = (stock) => {
  // 优先使用 lowPrice（后端返回的 JSON 字段名），然后尝试其他可能的字段名
  const low = stock.stock?.lowPrice || stock.stock?.low || stock.lowPrice || stock.low || 0
  // 如果最低价为0，使用当前价作为回退（非交易时间可能为0）
  const currentPrice = getStockPrice(stock)
  if (low > 0) {
    return low
  }
  // 如果最低价为0但有当前价，使用当前价
  if (currentPrice > 0) {
    return currentPrice
  }
  return 0
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.stock-cards {
  margin-top: 20px;
}

.category-group {
  margin-bottom: 30px;
}

.category-title {
  font-size: 1.2em;
  font-weight: bold;
  margin-bottom: 15px;
  padding-bottom: 8px;
  border-bottom: 2px solid #f0f0f0;
}

.stock-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 20px;
}

.stock-card {
  background: white;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 20px;
  position: relative;
  transition: all 0.3s;
  box-shadow: 0 2px 5px rgba(0,0,0,0.1);
}

.stock-card:hover {
  box-shadow: 0 4px 10px rgba(0,0,0,0.15);
  transform: translateY(-2px);
}

.stock-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 15px;
  padding-bottom: 10px;
  border-bottom: 2px solid #f0f0f0;
}

.stock-name {
  font-size: 1.5em;
  font-weight: bold;
  color: #333;
  margin-bottom: 5px;
}

.stock-code {
  font-size: 0.9em;
  color: #666;
}

.stock-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn-small {
  padding: 6px 12px;
  font-size: 0.85em;
}

.btn-info {
  background: #17a2b8;
}

.btn-info:hover {
  background: #138496;
}

.price-section {
  margin: 15px 0;
}

.current-price {
  font-size: 2em;
  font-weight: bold;
  margin-bottom: 5px;
}

.price-info-row {
  display: flex;
  gap: 15px;
  margin-top: 10px;
  font-size: 0.9em;
}

.price-item {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.price-label {
  color: #666;
  font-size: 0.85em;
}

.price-value {
  font-weight: bold;
}

.price-up {
  color: #f44336;
}

.price-down {
  color: #4caf50;
}

.cost-info {
  margin-top: 15px;
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 0.85em;
}

.cost-positive {
  background: #e8f5e9;
  color: #2e7d32;
}

.cost-negative {
  background: #ffebee;
  color: #c62828;
}

.cost-neutral {
  background: #f5f5f5;
  color: #666;
}

.modal {
  position: fixed;
  z-index: 1000;
  left: 0;
  top: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(0,0,0,0.5);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-content {
  background: white;
  border-radius: 8px;
  width: 90%;
  max-width: 500px;
  box-shadow: 0 4px 20px rgba(0,0,0,0.3);
}

.modal-header {
  padding: 20px 25px 15px;
  border-bottom: 1px solid #eee;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  margin: 0;
}

.close {
  color: #aaa;
  font-size: 28px;
  font-weight: bold;
  cursor: pointer;
}

.close:hover {
  color: #000;
}

.modal-body {
  padding: 20px 25px;
}

.modal-footer {
  padding: 15px 25px 20px;
  border-top: 1px solid #eee;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .stock-grid {
    grid-template-columns: 1fr;
  }
}
</style>

