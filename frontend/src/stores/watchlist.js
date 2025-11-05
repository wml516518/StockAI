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
      
      // 辅助函数：规范化股票代码（移除市场前缀，统一格式）
      const normalizeCode = (code) => {
        if (!code) return ''
        // 移除sh/sz前缀，统一转换为纯数字代码
        return code.toString().replace(/^(sh|sz)/i, '').trim()
      }
      
      // 更新价格信息
      stocks.value.forEach(stock => {
        const stockCodeNormalized = normalizeCode(stock.stockCode)
        
        // 查找匹配的更新数据
        const updated = updatedStocks.find(s => {
          const updatedCodeNormalized = normalizeCode(s.code || s.Code)
          return updatedCodeNormalized === stockCodeNormalized
        })
        
        if (updated) {
          // 确保stock对象存在
          if (!stock.stock) {
            stock.stock = {}
          }
          
          // 兼容PascalCase和camelCase两种命名方式
          // 后端可能返回: CurrentPrice/currentPrice, ChangeAmount/changeAmount 等
          const currentPrice = updated.currentPrice ?? updated.CurrentPrice
          const changeAmount = updated.changeAmount ?? updated.ChangeAmount
          const changePercent = updated.changePercent ?? updated.ChangePercent
          const highPrice = updated.highPrice ?? updated.HighPrice
          const lowPrice = updated.lowPrice ?? updated.LowPrice
          const openPrice = updated.openPrice ?? updated.OpenPrice
          const closePrice = updated.closePrice ?? updated.ClosePrice
          
          // 只有当更新数据中字段存在且有效时才更新，避免覆盖已有数据为0
          // 价格字段需要严格验证，防止错误的数据
          if (currentPrice !== undefined && currentPrice !== null && currentPrice > 0) {
            stock.stock.currentPrice = currentPrice
          }
          
          // 涨跌额可能为负数或0，所以只要不是undefined/null就更新
          if (changeAmount !== undefined && changeAmount !== null) {
            stock.stock.change = changeAmount
            stock.stock.changeAmount = changeAmount
          }
          
          if (changePercent !== undefined && changePercent !== null) {
            stock.stock.changePercent = changePercent
          }
          
          // 更新最高价和最低价，但只有在值有效时才更新
          // 注意：非交易时间这些值可能为0，所以只有在明确有值时才更新
          if (highPrice !== undefined && highPrice !== null && highPrice > 0) {
            stock.stock.highPrice = highPrice
            stock.stock.high = highPrice
          } else if (highPrice === 0 && stock.stock.currentPrice > 0) {
            // 只有在非交易时间（highPrice为0）且当前价有效时，才使用当前价作为回退
            // 这种情况主要发生在非交易时间
            stock.stock.highPrice = stock.stock.currentPrice
            stock.stock.high = stock.stock.currentPrice
          }
          
          if (lowPrice !== undefined && lowPrice !== null && lowPrice > 0) {
            stock.stock.lowPrice = lowPrice
            stock.stock.low = lowPrice
          } else if (lowPrice === 0 && stock.stock.currentPrice > 0) {
            // 只有在非交易时间（lowPrice为0）且当前价有效时，才使用当前价作为回退
            stock.stock.lowPrice = stock.stock.currentPrice
            stock.stock.low = stock.stock.currentPrice
          }
          
          // 更新股票名称（如果API返回了）
          if (updated.name || updated.Name) {
            stock.stock.name = updated.name || updated.Name
          }
          
          // 更新其他字段
          if (openPrice !== undefined && openPrice !== null) {
            stock.stock.openPrice = openPrice
          }
          if (closePrice !== undefined && closePrice !== null) {
            stock.stock.closePrice = closePrice
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

