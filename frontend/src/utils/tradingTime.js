/**
 * 股票交易时间工具函数
 * 中国A股交易时间：工作日（周一到周五）9:15-15:00
 */

/**
 * 判断当前是否在交易时间内
 * @returns {boolean} 如果在交易时间内返回true，否则返回false
 */
export function isTradingTime() {
  const now = new Date()
  const dayOfWeek = now.getDay() // 0=周日, 1=周一, ..., 6=周六
  const hours = now.getHours()
  const minutes = now.getMinutes()
  
  // 检查是否为工作日（周一到周五）
  if (dayOfWeek === 0 || dayOfWeek === 6) {
    return false // 周末不交易
  }
  
  // 检查时间是否在 9:15 到 15:00 之间
  const currentTime = hours * 60 + minutes // 转换为分钟数便于比较
  const openTime = 9 * 60 + 15 // 9:15 = 555分钟
  const closeTime = 15 * 60 // 15:00 = 900分钟
  
  return currentTime >= openTime && currentTime <= closeTime
}

/**
 * 获取下一个交易时间开始的时间（如果当前不在交易时间内）
 * @returns {Date|null} 返回下一个交易时间开始的Date对象，如果当前就在交易时间内则返回null
 */
export function getNextTradingTime() {
  if (isTradingTime()) {
    return null
  }
  
  const now = new Date()
  const dayOfWeek = now.getDay()
  const hours = now.getHours()
  const minutes = now.getMinutes()
  const currentTime = hours * 60 + minutes
  
  let nextTradingTime = new Date(now)
  
  // 如果当前是周末，需要等到下周一
  if (dayOfWeek === 0) {
    // 周日，下一个交易时间是下周一9:15
    const daysUntilMonday = 1
    nextTradingTime.setDate(now.getDate() + daysUntilMonday)
    nextTradingTime.setHours(9, 15, 0, 0)
  } else if (dayOfWeek === 6) {
    // 周六，下一个交易时间是下周一9:15
    const daysUntilMonday = 2
    nextTradingTime.setDate(now.getDate() + daysUntilMonday)
    nextTradingTime.setHours(9, 15, 0, 0)
  } else {
    // 工作日
    if (currentTime < 9 * 60 + 15) {
      // 早于9:15，今天9:15开始交易
      nextTradingTime.setHours(9, 15, 0, 0)
    } else if (currentTime > 15 * 60) {
      // 晚于15:00，明天9:15开始交易
      nextTradingTime.setDate(now.getDate() + 1)
      nextTradingTime.setHours(9, 15, 0, 0)
    }
  }
  
  return nextTradingTime
}

/**
 * 获取当前交易状态描述
 * @returns {string} 交易状态描述文本
 */
export function getTradingStatusText() {
  if (isTradingTime()) {
    const now = new Date()
    const hours = now.getHours()
    const minutes = now.getMinutes()
    return `交易中 (${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')})`
  }
  
  const nextTime = getNextTradingTime()
  if (nextTime) {
    const now = new Date()
    const diffMs = nextTime.getTime() - now.getTime()
    const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
    const diffMinutes = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60))
    
    if (diffHours > 0) {
      return `休市中 (${diffHours}小时${diffMinutes}分钟后开市)`
    } else {
      return `休市中 (${diffMinutes}分钟后开市)`
    }
  }
  
  return '休市中'
}

