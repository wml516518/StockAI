import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { watchlistService } from '../services/watchlistService'
import { stockService } from '../services/stockService'
import { isTradingTime } from '../utils/tradingTime'

export const useWatchlistStore = defineStore('watchlist', () => {
  const INSIGHTS_STORAGE_KEY = 'ai_stock_insights'
  const INSIGHTS_TTL_MS = 2 * 24 * 60 * 60 * 1000 // 2 days

  const readStoredInsights = () => {
    if (typeof window === 'undefined' || !window?.localStorage) {
      return {}
    }
    try {
      const raw = window.localStorage.getItem(INSIGHTS_STORAGE_KEY)
      if (!raw) {
        return {}
      }
      const parsed = JSON.parse(raw)
      if (!parsed || typeof parsed !== 'object') {
        return {}
      }
      const now = Date.now()
      const valid = {}
      Object.entries(parsed).forEach(([code, insight]) => {
        if (!insight || typeof insight !== 'object') {
          return
        }
        const updatedAtMs = insight.updatedAt ? new Date(insight.updatedAt).getTime() : NaN
        if (Number.isNaN(updatedAtMs) || now - updatedAtMs > INSIGHTS_TTL_MS) {
          return
        }
        valid[code] = {
          rating: insight.rating ?? null,
          actionSuggestion: insight.actionSuggestion ?? null,
          updatedAt: new Date(updatedAtMs).toISOString()
        }
      })
      return valid
    } catch (error) {
      console.warn('[watchlist] 读取AI分析缓存失败:', error)
      return {}
    }
  }

  const persistInsights = (insights) => {
    if (typeof window === 'undefined' || !window?.localStorage) {
      return
    }
    try {
      window.localStorage.setItem(INSIGHTS_STORAGE_KEY, JSON.stringify(insights))
    } catch (error) {
      console.warn('[watchlist] 写入AI分析缓存失败:', error)
    }
  }

  const stocks = ref([])
  const categories = ref([])
  const loading = ref(false)
  const autoRefreshEnabled = ref(true)
  const refreshInterval = ref(3)
  const stockInsights = ref(readStoredInsights())

  const normalizeStockCode = (code) => {
    if (!code) return ''
    return code.toString().trim().toUpperCase()
  }

  const applyInsightsToStocks = () => {
    if (!stocks.value?.length) {
      return
    }
    stocks.value.forEach(stock => {
      const code = normalizeStockCode(stock.stockCode)
      const insight = stockInsights.value[code]
      if (insight) {
        stock.aiRating = insight.rating
        stock.aiActionSuggestion = insight.actionSuggestion
        stock.aiUpdatedAt = insight.updatedAt
      }
    })
  }

  // 计算属性
  const stocksByCategory = computed(() => {
    const grouped = {}
    stocks.value.forEach(stock => {
      // 兼容PascalCase和camelCase两种命名方式
      const category = stock.category || stock.Category
      const categoryName = category?.name || category?.Name || '未分类'
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
      console.log('开始获取自选股...')
      const response = await watchlistService.getWatchlist()
      console.log('API返回的原始数据:', response)
      console.log('数据类型:', typeof response)
      console.log('是否为数组:', Array.isArray(response))
      
      let dataArray = []
      
      if (Array.isArray(response)) {
        dataArray = response
      } else if (response && typeof response === 'object') {
        // 如果返回的是对象，尝试提取数组
        dataArray = response.data || response.items || response.stocks || []
      }
      
      stocks.value = dataArray
      console.log('最终设置的自选股数量:', stocks.value.length)

      applyInsightsToStocks()
      
      if (stocks.value.length > 0) {
        console.log('第一条股票数据:', JSON.stringify(stocks.value[0], null, 2))
      } else {
        console.warn('自选股列表为空，可能的原因：')
        console.warn('1. 数据库中确实没有自选股数据')
        console.warn('2. API返回的数据格式不正确')
        console.warn('3. 需要先添加自选股')
      }
    } catch (error) {
      console.error('获取自选股失败:', error)
      console.error('错误类型:', error.constructor.name)
      console.error('错误消息:', error.message)
      if (error.response) {
        console.error('响应状态:', error.response.status)
        console.error('响应数据:', error.response.data)
      }
      stocks.value = []
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
      const newStock = await watchlistService.addStock(stockCode, categoryId, costPrice, quantity)
      // 直接将新股票添加到列表，不重新获取整个列表
      // 获取实时行情数据（单独获取，不触发批量刷新）
      const realTimeStock = await stockService.getStock(stockCode)
      if (realTimeStock) {
        newStock.stock = realTimeStock
      }
      // 添加到列表
      stocks.value.push(newStock)
      applyInsightsToStocks()
      // 注意：不要在这里调用 refreshPrices，因为自动刷新定时器会处理
      return newStock
    } catch (error) {
      console.error('添加自选股失败:', error)
      // 保持原始错误信息，让调用方处理
      throw error
    }
  }

  // 删除自选股
  async function removeStock(id) {
    try {
      await watchlistService.removeStock(id)
      // 只从列表中移除，不重新获取整个列表
      const index = stocks.value.findIndex(s => s.id === id)
      if (index !== -1) {
        const removed = stocks.value.splice(index, 1)[0]
        if (removed?.stockCode) {
          delete stockInsights.value[normalizeStockCode(removed.stockCode)]
        }
      }
    } catch (error) {
      console.error('删除自选股失败:', error)
      throw error
    }
  }

  // 更新自选股
  async function updateStock(id, costPrice, quantity) {
    try {
      const updatedStock = await watchlistService.updateStock(id, costPrice, quantity)
      // 只更新对应的股票项，不重新获取整个列表
      const index = stocks.value.findIndex(s => s.id === id)
      if (index !== -1) {
        // 更新成本相关字段
        stocks.value[index].costPrice = updatedStock.costPrice
        stocks.value[index].quantity = updatedStock.quantity
        stocks.value[index].totalCost = updatedStock.totalCost
        stocks.value[index].profitLoss = updatedStock.profitLoss
        stocks.value[index].profitLossPercent = updatedStock.profitLossPercent
        stocks.value[index].lastUpdate = updatedStock.lastUpdate
      }
      return updatedStock
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

  // 删除分类
  async function deleteCategory(id) {
    try {
      await watchlistService.deleteCategory(id)
      const index = categories.value.findIndex(c => c.id === id || c.Id === id)
      if (index !== -1) {
        categories.value.splice(index, 1)
      }
      // 将已删除分类的股票移动到未分类
      stocks.value.forEach(stock => {
        const stockCategoryId = stock.watchlistCategoryId || stock.category?.id || stock.Category?.id
        if (stockCategoryId === id) {
          stock.watchlistCategoryId = null
          if (stock.category) {
            stock.category = null
          }
          if (stock.Category) {
            stock.Category = null
          }
        }
      })
    } catch (error) {
      console.error('删除分类失败:', error)
      throw error
    }
  }

  // 更新自选股分类
  async function updateCategory(id, categoryId) {
    try {
      const updatedStock = await watchlistService.updateCategory(id, categoryId)
      // 只更新对应的股票项，不重新获取整个列表
      const index = stocks.value.findIndex(s => s.id === id)
      if (index !== -1) {
        // 更新分类相关字段
        stocks.value[index].watchlistCategoryId = updatedStock.watchlistCategoryId
        stocks.value[index].category = updatedStock.category || updatedStock.Category
        stocks.value[index].lastUpdate = updatedStock.lastUpdate
      }
      return updatedStock
    } catch (error) {
      console.error('更新分类失败:', error)
      throw error
    }
  }

  // 更新建议价格
  async function updateSuggestedPrice(id, suggestedBuyPrice, suggestedSellPrice) {
    try {
      const updatedStock = await watchlistService.updateSuggestedPrice(id, suggestedBuyPrice, suggestedSellPrice)
      // 只更新对应的股票项，不重新获取整个列表
      const index = stocks.value.findIndex(s => s.id === id)
      if (index !== -1) {
        // 更新建议价格字段，保留其他数据不变
        stocks.value[index].suggestedBuyPrice = updatedStock.suggestedBuyPrice
        stocks.value[index].suggestedSellPrice = updatedStock.suggestedSellPrice
        stocks.value[index].buyAlertSent = updatedStock.buyAlertSent
        stocks.value[index].sellAlertSent = updatedStock.sellAlertSent
        stocks.value[index].lastUpdate = updatedStock.lastUpdate
      }
      return updatedStock
    } catch (error) {
      console.error('更新建议价格失败:', error)
      throw error
    }
  }

  function setStockRecommendation(stockCode, rating, actionSuggestion) {
    const code = normalizeStockCode(stockCode)
    if (!code) {
      return
    }

    const normalizedRating = rating ? rating.trim() : null
    const normalizedSuggestion = actionSuggestion ? actionSuggestion.trim() : null

    if (normalizedRating || normalizedSuggestion) {
      const updatedInsight = {
        rating: normalizedRating,
        actionSuggestion: normalizedSuggestion,
        updatedAt: new Date().toISOString()
      }
      stockInsights.value[code] = updatedInsight
      persistInsights(stockInsights.value)
    } else {
      delete stockInsights.value[code]
      persistInsights(stockInsights.value)
    }

    const target = stocks.value.find(
      s => normalizeStockCode(s.stockCode) === code
    )
    if (target) {
      target.aiRating = normalizedRating
      target.aiActionSuggestion = normalizedSuggestion
      target.aiUpdatedAt = new Date().toISOString()
    } else {
      applyInsightsToStocks()
    }
  }

  // 刷新股票价格
  // 防止并发刷新
  let isRefreshing = false
  
  async function refreshPrices(forceRefresh = false) {
    // 如果正在刷新或没有股票，直接返回
    if (isRefreshing || stocks.value.length === 0) {
      return
    }
    
    // 如果是自动刷新（非强制），需要检查是否启用和是否在交易时间内
    if (!forceRefresh) {
      // 自动刷新需要启用状态
      if (!autoRefreshEnabled.value) {
        return
      }
      
      // 检查是否在交易时间内，如果不在交易时间内则不刷新
      if (!isTradingTime()) {
        console.log('当前不在交易时间内，跳过自动刷新')
        return
      }
    }
    
    try {
      isRefreshing = true
      const codes = stocks.value.map(s => s.stockCode)
      if (codes.length === 0) {
        isRefreshing = false
        return
      }
      
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
          
          // 检查并重置提醒标志（当价格偏离建议价格时）
          if (currentPrice > 0) {
            // 如果当前价格高于建议买入价，重置买入提醒
            if (stock.suggestedBuyPrice && stock.buyAlertSent && currentPrice > stock.suggestedBuyPrice) {
              stock.buyAlertSent = false
              // 异步调用API更新后端，不等待结果（避免阻塞刷新）
              watchlistService.resetAlertFlags(stock.id, currentPrice).catch(err => {
                console.warn('重置买入提醒标志失败:', err)
              })
            }
            
            // 如果当前价格低于建议卖出价，重置卖出提醒
            if (stock.suggestedSellPrice && stock.sellAlertSent && currentPrice < stock.suggestedSellPrice) {
              stock.sellAlertSent = false
              // 异步调用API更新后端，不等待结果（避免阻塞刷新）
              watchlistService.resetAlertFlags(stock.id, currentPrice).catch(err => {
                console.warn('重置卖出提醒标志失败:', err)
              })
            }
          }
        }
      })
    } catch (error) {
      console.error('刷新价格失败:', error)
    } finally {
      isRefreshing = false
    }
  }

  return {
    stocks,
    categories,
    loading,
    autoRefreshEnabled,
    refreshInterval,
    stocksByCategory,
    stockInsights,
    fetchWatchlist,
    fetchCategories,
    addStock,
    removeStock,
    updateStock,
    createCategory,
    deleteCategory,
    updateCategory,
    updateSuggestedPrice,
    refreshPrices,
    setStockRecommendation
  }
})

