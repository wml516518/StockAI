import api from './api'

export const backtestService = {
  // 一键回测
  quickBacktest(stockCode, startDate, endDate, initialCapital = 100000) {
    return api.post('/simplebacktest/quick-test', {
      stockCode,
      startDate,
      endDate,
      initialCapital
    })
  },

  // 批量一键回测
  quickBatchBacktest(stockCodes, startDate, endDate, initialCapital = 100000) {
    return api.post('/simplebacktest/quick-batch-test', {
      stockCodes,
      startDate,
      endDate,
      initialCapital
    })
  },

  // 批量回测
  runBatchBacktest(strategyName, stockCodes, startDate, endDate, initialCapital = 100000) {
    return api.post('/backtest/run-batch', {
      strategyName,
      stockCodes,
      startDate,
      endDate,
      initialCapital
    })
  }
}

