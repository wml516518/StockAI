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
              间隔: <span>{{ refreshInterval }}秒</span> |
              交易状态: <span :style="{ color: isTradingTimeNow ? '#4caf50' : '#999' }">{{ tradingStatusText }}</span>
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
                    <select 
                      :value="stock.watchlistCategoryId || stock.category?.id || stock.Category?.id" 
                      @change="handleCategoryChange(stock.id, $event.target.value)"
                      class="category-select"
                      title="切换分类"
                    >
                      <option v-for="cat in categories" :key="cat.id" :value="cat.id">
                        {{ cat.name || cat.Name }}
                      </option>
                    </select>
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
                <div class="suggested-price-section">
                  <div class="suggested-price-header">
                    <span>建议价格</span>
                    <button 
                      class="btn-icon" 
                      @click="toggleSuggestedPriceEdit(stock.id)"
                      :title="editingSuggestedPrice[stock.id] ? '取消编辑' : '编辑建议价格'"
                    >
                      {{ editingSuggestedPrice[stock.id] ? '✕' : '✎' }}
                    </button>
                  </div>
                  <div v-if="editingSuggestedPrice[stock.id]" class="suggested-price-edit">
                    <div class="price-input-group">
                      <label>买入价:</label>
                      <input 
                        type="number" 
                        step="0.01" 
                        v-model.number="suggestedPriceForm[stock.id].buyPrice"
                        placeholder="建议买入价"
                        class="price-input"
                      />
                    </div>
                    <div class="price-input-group">
                      <label>卖出价:</label>
                      <input 
                        type="number" 
                        step="0.01" 
                        v-model.number="suggestedPriceForm[stock.id].sellPrice"
                        placeholder="建议卖出价"
                        class="price-input"
                      />
                    </div>
                    <button 
                      class="btn btn-small" 
                      @click="handleSaveSuggestedPrice(stock.id)"
                      :disabled="savingSuggestedPrice[stock.id]"
                    >
                      {{ savingSuggestedPrice[stock.id] ? '保存中...' : '保存' }}
                    </button>
                  </div>
                  <div v-else class="suggested-price-display">
                    <div v-if="stock.suggestedBuyPrice" class="suggested-price-item buy-price">
                      <span class="price-label">买入:</span>
                      <span class="price-value">{{ formatPrice(stock.suggestedBuyPrice) }}</span>
                      <span v-if="stock.buyAlertSent" class="alert-badge" title="已达到买入价，已提醒">✓</span>
                      <span v-else-if="getStockPrice(stock) > 0 && getStockPrice(stock) <= stock.suggestedBuyPrice" class="alert-badge alert-triggered" title="当前价格已达到买入价">🔔</span>
                    </div>
                    <div v-if="stock.suggestedSellPrice" class="suggested-price-item sell-price">
                      <span class="price-label">卖出:</span>
                      <span class="price-value">{{ formatPrice(stock.suggestedSellPrice) }}</span>
                      <span v-if="stock.sellAlertSent" class="alert-badge" title="已达到卖出价，已提醒">✓</span>
                      <span v-else-if="getStockPrice(stock) > 0 && getStockPrice(stock) >= stock.suggestedSellPrice" class="alert-badge alert-triggered" title="当前价格已达到卖出价">🔔</span>
                    </div>
                    <div v-if="!stock.suggestedBuyPrice && !stock.suggestedSellPrice" class="no-suggested-price">
                      未设置建议价格
                    </div>
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
import { isTradingTime, getTradingStatusText } from '../utils/tradingTime'

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
let tradingStatusTimer = null

// 交易状态相关
const isTradingTimeNow = ref(isTradingTime())
const tradingStatusText = ref(getTradingStatusText())

// 建议价格编辑相关
const editingSuggestedPrice = ref({})
const suggestedPriceForm = ref({})
const savingSuggestedPrice = ref({})

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
  // 更新交易状态
  updateTradingStatus()
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
    // 直接更新 store 中的 ref，避免写入 computed 属性
    watchlistStore.$patch({ refreshInterval: interval })
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
  // 先清除现有定时器，避免重复创建
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
  
  if (autoRefreshEnabled.value) {
    const intervalSeconds = refreshInterval.value || watchlistStore.refreshInterval || 3
    console.log('启动自动刷新，间隔:', intervalSeconds, '秒')
    refreshTimer = setInterval(() => {
      // 只有在有股票且在交易时间内时才刷新
      if (watchlistStore.stocks.length > 0 && isTradingTime()) {
        watchlistStore.refreshPrices()
      }
    }, intervalSeconds * 1000)
  }
  
  // 启动交易状态更新定时器（每分钟更新一次）
  if (!tradingStatusTimer) {
    updateTradingStatus()
    tradingStatusTimer = setInterval(() => {
      updateTradingStatus()
    }, 60000) // 每分钟更新一次
  }
}

const updateTradingStatus = () => {
  isTradingTimeNow.value = isTradingTime()
  tradingStatusText.value = getTradingStatusText()
}

const stopAutoRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
  if (tradingStatusTimer) {
    clearInterval(tradingStatusTimer)
    tradingStatusTimer = null
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
    // 提取友好的错误消息
    let errorMessage = '添加失败，请稍后重试'
    
    if (error.response) {
      const responseData = error.response.data
      
      // 后端返回的字符串错误消息（如："该股票已存在于此分类"）
      if (typeof responseData === 'string' && responseData.trim()) {
        errorMessage = responseData
      } 
      // JSON格式的错误响应
      else if (responseData && typeof responseData === 'object') {
        errorMessage = responseData.message || responseData.error || errorMessage
      }
    } else if (error.message && !error.message.includes('status code')) {
      // 如果不是技术性错误消息，使用原始消息
      errorMessage = error.message
    }
    
    // 显示友好的错误提示
    alert(errorMessage)
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

const handleCategoryChange = async (stockId, categoryId) => {
  try {
    await watchlistStore.updateCategory(stockId, parseInt(categoryId))
  } catch (error) {
    alert('更新分类失败: ' + (error.response?.data?.message || error.message))
    // 如果失败，重新加载数据以恢复原状态
    await watchlistStore.fetchWatchlist()
  }
}

const toggleSuggestedPriceEdit = (stockId) => {
  if (editingSuggestedPrice.value[stockId]) {
    // 取消编辑
    delete editingSuggestedPrice.value[stockId]
    delete suggestedPriceForm.value[stockId]
  } else {
    // 开始编辑
    const stock = stocks.value.find(s => s.id === stockId)
    editingSuggestedPrice.value[stockId] = true
    suggestedPriceForm.value[stockId] = {
      buyPrice: stock?.suggestedBuyPrice || null,
      sellPrice: stock?.suggestedSellPrice || null
    }
  }
}

const handleSaveSuggestedPrice = async (stockId) => {
  try {
    savingSuggestedPrice.value[stockId] = true
    const form = suggestedPriceForm.value[stockId]
    await watchlistStore.updateSuggestedPrice(
      stockId,
      form.buyPrice || null,
      form.sellPrice || null
    )
    // 立即关闭编辑模式，不等待列表刷新
    delete editingSuggestedPrice.value[stockId]
    delete suggestedPriceForm.value[stockId]
  } catch (error) {
    alert('保存建议价格失败: ' + (error.response?.data?.message || error.message))
  } finally {
    delete savingSuggestedPrice.value[stockId]
  }
}

const getCategoryColor = (categoryName) => {
  const category = categories.value.find(c => (c.name || c.Name) === categoryName)
  return category?.color || category?.Color || '#667eea'
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

.category-select {
  padding: 6px 12px;
  font-size: 0.85em;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: white;
  cursor: pointer;
  min-width: 100px;
  transition: all 0.3s;
}

.category-select:hover {
  border-color: #1890ff;
}

.category-select:focus {
  outline: none;
  border-color: #1890ff;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
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

.suggested-price-section {
  margin-top: 15px;
  padding: 12px;
  background: #f9f9f9;
  border-radius: 6px;
  border: 1px solid #e0e0e0;
}

.suggested-price-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  font-weight: bold;
  font-size: 0.9em;
  color: #333;
}

.btn-icon {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 1.2em;
  color: #666;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 0.2s;
}

.btn-icon:hover {
  background: #e0e0e0;
  color: #333;
}

.suggested-price-edit {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.price-input-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.price-input-group label {
  min-width: 60px;
  font-size: 0.85em;
  color: #666;
}

.price-input {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 0.9em;
}

.price-input:focus {
  outline: none;
  border-color: #1890ff;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
}

.suggested-price-display {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.suggested-price-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9em;
  padding: 4px 0;
}

.suggested-price-item.buy-price .price-value {
  color: #4caf50;
  font-weight: bold;
}

.suggested-price-item.sell-price .price-value {
  color: #f44336;
  font-weight: bold;
}

.price-label {
  min-width: 50px;
  color: #666;
}

.price-value {
  flex: 1;
}

.alert-badge {
  color: #4caf50;
  font-weight: bold;
  font-size: 1.1em;
}

.alert-badge.alert-triggered {
  color: #ff9800;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}

.no-suggested-price {
  color: #999;
  font-size: 0.85em;
  font-style: italic;
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

