import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { watchlistService } from '../services/watchlistService'
import { stockService } from '../services/stockService'

export const useWatchlistStore = defineStore('watchlist', () => {
  const stocks = ref([])
  const categories = ref([])
  const loading = ref(false)
  const autoRefreshEnabled = ref(true)
  const refreshInterval = ref(3)

  // 计算属性
  const stocksByCategory = computed(() => {
    const grouped = {}
    stocks.value.forEach(stock => {
      const categoryName = stock.category?.name || '未分类'
      if (!grouped[categoryName]) {
        grouped[categoryName] = []
      }
      grouped[categoryName].push(stock)
    })
    return grouped
  })

  // 获取自选股列表
  async function fetchWatchlist() {
    loading.value = true
    try {
      stocks.value = await watchlistService.getWatchlist()
    } catch (error) {
      console.error('获取自选股失败:', error)
    } finally {
      loading.value = false
    }
  }

  // 获取分类列表
  async function fetchCategories() {
    try {
      categories.value = await watchlistService.getCategories()
    } catch (error) {
      console.error('获取分类失败:', error)
    }
  }

  // 添加自选股
  async function addStock(stockCode, categoryId, costPrice, quantity) {
    try {
      await watchlistService.addStock(stockCode, categoryId, costPrice, quantity)
      await fetchWatchlist()
    } catch (error) {
      console.error('添加自选股失败:', error)
      throw error
    }
  }

  // 删除自选股
  async function removeStock(id) {
    try {
      await watchlistService.removeStock(id)
      await fetchWatchlist()
    } catch (error) {
      console.error('删除自选股失败:', error)
      throw error
    }
  }

  // 更新自选股
  async function updateStock(id, costPrice, quantity) {
    try {
      await watchlistService.updateStock(id, costPrice, quantity)
      await fetchWatchlist()
    } catch (error) {
      console.error('更新自选股失败:', error)
      throw error
    }
  }

  // 创建分类
  async function createCategory(name, description, color) {
    try {
      await watchlistService.createCategory(name, description, color)
      await fetchCategories()
    } catch (error) {
      console.error('创建分类失败:', error)
      throw error
    }
  }

  // 刷新股票价格
  async function refreshPrices() {
    if (!autoRefreshEnabled.value || stocks.value.length === 0) return
    
    try {
      const codes = stocks.value.map(s => s.stockCode)
      if (codes.length === 0) return
      
      const updatedStocks = await stockService.getBatchStocks(codes)
      
      // 更新价格信息
      stocks.value.forEach(stock => {
        const updated = updatedStocks.find(s => {
          // 处理不同的代码格式（可能带市场前缀）
          const updatedCode = s.code?.replace(/^(sh|sz)/i, '') || s.code
          const stockCode = stock.stockCode?.replace(/^(sh|sz)/i, '') || stock.stockCode
          return updatedCode === stockCode
        })
        
        if (updated) {
          // 更新股票对象的价格信息
          if (!stock.stock) {
            stock.stock = {}
          }
          stock.stock.currentPrice = updated.price || updated.currentPrice || 0
          stock.stock.change = updated.change || updated.changeAmount || 0
          stock.stock.changePercent = updated.changePercent || 0
          // 使用正确的字段名：highPrice 和 lowPrice（后端返回的 JSON 字段名）
          stock.stock.highPrice = updated.highPrice || stock.stock.highPrice || 0
          stock.stock.lowPrice = updated.lowPrice || stock.stock.lowPrice || 0
          // 同时保留旧的字段名以兼容
          stock.stock.high = stock.stock.highPrice
          stock.stock.low = stock.stock.lowPrice
          // 如果最高价或最低价为0，使用当前价作为回退
          if (stock.stock.high === 0 && stock.stock.currentPrice > 0) {
            stock.stock.high = stock.stock.currentPrice
          }
          if (stock.stock.low === 0 && stock.stock.currentPrice > 0) {
            stock.stock.low = stock.stock.currentPrice
          }
          // 同时更新股票名称（如果API返回了）
          if (updated.name) {
            stock.stock.name = updated.name
          }
        }
      })
    } catch (error) {
      console.error('刷新价格失败:', error)
    }
  }

  return {
    stocks,
    categories,
    loading,
    autoRefreshEnabled,
    refreshInterval,
    stocksByCategory,
    fetchWatchlist,
    fetchCategories,
    addStock,
    removeStock,
    updateStock,
    createCategory,
    refreshPrices
  }
})

