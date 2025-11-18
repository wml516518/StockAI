<template>
  <div ref="chartContainer" class="stock-chart-container"></div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import * as echarts from 'echarts'

const props = defineProps({
  data: {
    type: Array,
    default: () => []
  },
  highlights: {
    type: Object,
    default: () => ({})
  },
  stockName: {
    type: String,
    default: ''
  },
  // 支持多股票数据，格式: [{ stockCode: '000001', stockName: '平安银行', data: [...] }, ...]
  multiStockData: {
    type: Array,
    default: () => []
  }
})

const chartContainer = ref(null)
let chartInstance = null
// 保存原始数据引用，供tooltip使用
let currentChartData = []

// 计算移动平均线
const calculateMA = (data, period) => {
  const result = []
  for (let i = 0; i < data.length; i++) {
    if (i < period - 1) {
      result.push(null)
    } else {
      let sum = 0
      for (let j = i - period + 1; j <= i; j++) {
        // 支持大小写字段名
        const close = data[j].close ?? data[j].Close ?? 0
        sum += Number(close)
      }
      result.push((sum / period).toFixed(2))
    }
  }
  return result
}

// 格式化日期
const formatDate = (dateStr) => {
  if (!dateStr) return ''
  const date = new Date(dateStr)
  if (isNaN(date.getTime())) return dateStr
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${month}-${day}`
}

// 初始化图表
const initChart = () => {
  if (!chartContainer.value) return

  chartInstance = echarts.init(chartContainer.value)

  const option = {
    title: {
      text: props.stockName ? `${props.stockName} - 股价走势（含主要均线）` : '股价走势（含主要均线）',
      left: 'center',
      textStyle: {
        fontSize: 16,
        fontWeight: 'bold'
      }
    },
    tooltip: {
      trigger: 'axis',
      axisPointer: {
        type: 'cross'
      },
      formatter: function (params) {
        if (!params || params.length === 0) return ''
        
        let result = `<div style="margin-bottom: 4px;"><strong>${params[0].axisValue}</strong></div>`
        
        params.forEach(param => {
          if (param.seriesName === 'K线') {
            // ECharts candlestick 数据格式: [开盘, 收盘, 最低, 最高]
            // 在axis trigger模式下，优先从原始数据中获取，因为param.data可能不是数组格式
            let openVal, closeVal, lowVal, highVal
            
            // 优先从原始数据中获取（最可靠的方式）
            const dataIndex = param.dataIndex
            // 使用保存的原始数据引用，确保能够访问
            const sourceData = currentChartData.length > 0 ? currentChartData : (props.data || [])
            
            if (dataIndex !== undefined && sourceData && Array.isArray(sourceData) && sourceData[dataIndex]) {
              const item = sourceData[dataIndex]
              openVal = item.open ?? item.Open ?? 0
              closeVal = item.close ?? item.Close ?? 0
              lowVal = item.low ?? item.Low ?? 0
              highVal = item.high ?? item.High ?? 0
              
              // 调试：打印从原始数据获取的值
              if (process.env.NODE_ENV === 'development') {
                console.log(`[K线Tooltip] 从原始数据[${dataIndex}]获取:`, { 
                  openVal, closeVal, lowVal, highVal,
                  rawItem: item,
                  paramData: param.data,
                  paramValue: param.value,
                  sourceDataLength: sourceData.length
                })
              }
            } else if (Array.isArray(param.data) && param.data.length >= 4) {
              // 数组格式: [开盘, 收盘, 最低, 最高]
              openVal = param.data[0]
              closeVal = param.data[1]
              lowVal = param.data[2]
              highVal = param.data[3]
              
              // 调试：打印解析后的值
              if (process.env.NODE_ENV === 'development') {
                console.log('[K线Tooltip] 从param.data数组解析:', { openVal, closeVal, lowVal, highVal })
              }
            } else if (Array.isArray(param.value) && param.value.length >= 4) {
              // 某些ECharts版本可能使用param.value
              openVal = param.value[0]
              closeVal = param.value[1]
              lowVal = param.value[2]
              highVal = param.value[3]
              
              // 调试：打印从param.value获取的值
              if (process.env.NODE_ENV === 'development') {
                console.log('[K线Tooltip] 从param.value数组解析:', { openVal, closeVal, lowVal, highVal })
              }
            } else if (param.data && typeof param.data === 'object') {
              // 对象格式（备用）
              openVal = param.data[0] ?? param.data.open ?? param.data.Open
              closeVal = param.data[1] ?? param.data.close ?? param.data.Close
              lowVal = param.data[2] ?? param.data.low ?? param.data.Low
              highVal = param.data[3] ?? param.data.high ?? param.data.High
              
              // 调试：打印从对象获取的值
              if (process.env.NODE_ENV === 'development') {
                console.log('[K线Tooltip] 从param.data对象解析:', { openVal, closeVal, lowVal, highVal })
              }
            } else {
              // 如果所有方式都失败，使用默认值并记录警告
              console.warn('[K线Tooltip] 无法获取数据，使用默认值', {
                dataIndex,
                paramData: param.data,
                paramValue: param.value,
                sourceDataLength: sourceData.length,
                propsDataLength: props.data?.length
              })
              openVal = 0
              closeVal = 0
              lowVal = 0
              highVal = 0
            }
            
            const open = (openVal !== null && openVal !== undefined && !isNaN(Number(openVal))) 
              ? Number(openVal).toFixed(2) : 'N/A'
            const close = (closeVal !== null && closeVal !== undefined && !isNaN(Number(closeVal))) 
              ? Number(closeVal).toFixed(2) : 'N/A'
            const low = (lowVal !== null && lowVal !== undefined && !isNaN(Number(lowVal))) 
              ? Number(lowVal).toFixed(2) : 'N/A'
            const high = (highVal !== null && highVal !== undefined && !isNaN(Number(highVal))) 
              ? Number(highVal).toFixed(2) : 'N/A'
            
            result += `
              <div style="margin: 2px 0;">
                <span style="display:inline-block;width:10px;height:10px;background:${param.color};border-radius:2px;margin-right:5px;"></span>
                <strong>${param.seriesName}:</strong><br/>
                &nbsp;&nbsp;开盘: ${open} 元<br/>
                &nbsp;&nbsp;收盘: ${close} 元<br/>
                &nbsp;&nbsp;最低: ${low} 元<br/>
                &nbsp;&nbsp;最高: ${high} 元
              </div>
            `
          } else if (param.seriesName !== 'K线') {
            const value = param.value !== null && param.value !== undefined && !isNaN(Number(param.value)) 
              ? Number(param.value).toFixed(2) : 'N/A'
            result += `
              <div style="margin: 2px 0;">
                <span style="display:inline-block;width:10px;height:10px;background:${param.color};border-radius:2px;margin-right:5px;"></span>
                <strong>${param.seriesName}:</strong> ${value} 元
              </div>
            `
          }
        })
        
        return result
      },
      backgroundColor: 'rgba(50, 50, 50, 0.9)',
      borderColor: '#333',
      borderWidth: 1,
      textStyle: {
        color: '#fff',
        fontSize: 12
      },
      padding: [8, 12]
    },
    legend: {
      data: ['K线', 'MA5', 'MA10', 'MA20', 'MA60'],
      top: 35,
      left: 'center'
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: '10%',
      top: '15%',
      containLabel: true
    },
    xAxis: {
      type: 'category',
      data: [],
      boundaryGap: false,
      axisLine: { onZero: false },
      splitLine: { show: false },
      min: 'dataMin',
      max: 'dataMax',
      axisLabel: {
        formatter: function (value) {
          return formatDate(value)
        },
        rotate: 45
      }
    },
    yAxis: {
      scale: true,
      splitArea: {
        show: true
      },
      axisLabel: {
        formatter: '{value} 元'
      }
    },
    dataZoom: [
      {
        type: 'inside',
        start: 0,
        end: 100
      },
      {
        show: true,
        type: 'slider',
        top: '90%',
        start: 0,
        end: 100
      }
    ],
    series: [
      {
        name: 'K线',
        type: 'candlestick',
        data: [],
        itemStyle: {
          color: '#ef5350',
          color0: '#26a69a',
          borderColor: '#ef5350',
          borderColor0: '#26a69a'
        }
      },
      {
        name: 'MA5',
        type: 'line',
        data: [],
        smooth: true,
        lineStyle: {
          width: 1.5,
          color: '#ff7f0e'
        },
        symbol: 'none'
      },
      {
        name: 'MA10',
        type: 'line',
        data: [],
        smooth: true,
        lineStyle: {
          width: 1.5,
          color: '#2ca02c'
        },
        symbol: 'none'
      },
      {
        name: 'MA20',
        type: 'line',
        data: [],
        smooth: true,
        lineStyle: {
          width: 1.5,
          color: '#9467bd'
        },
        symbol: 'none'
      },
      {
        name: 'MA60',
        type: 'line',
        data: [],
        smooth: true,
        lineStyle: {
          width: 1.5,
          color: '#8c564b'
        },
        symbol: 'none'
      }
    ]
  }

  chartInstance.setOption(option)
  
  // 响应式调整
  window.addEventListener('resize', handleResize)
}

// 更新图表数据
const updateChart = () => {
  if (!chartInstance) {
    console.warn('[StockChart] chartInstance不存在，无法更新图表')
    return
  }

  // 调试信息
  if (process.env.NODE_ENV === 'development') {
    console.log('[StockChart] updateChart调用:', {
      hasData: !!props.data,
      dataLength: props.data?.length || 0,
      hasMultiStockData: !!props.multiStockData,
      multiStockDataLength: props.multiStockData?.length || 0,
      firstDataItem: props.data?.[0]
    })
  }

  // 优先使用单股票模式（props.data）
  if (props.data && Array.isArray(props.data) && props.data.length > 0) {
    updateSingleStockChart()
  } else if (props.multiStockData && Array.isArray(props.multiStockData) && props.multiStockData.length > 0) {
    // 多股票模式：为每只股票创建单独的系列（备用）
    updateMultiStockChart()
  } else {
    // 无数据，清空图表
    if (process.env.NODE_ENV === 'development') {
      console.warn('[StockChart] 无数据，清空图表')
    }
    chartInstance.setOption({
      xAxis: { data: [] },
      series: []
    })
  }
}

// 单股票模式更新
const updateSingleStockChart = () => {
  if (!props.data || !Array.isArray(props.data) || props.data.length === 0) {
    console.warn('[StockChart] updateSingleStockChart: 数据无效', props.data)
    return
  }

  // 按日期排序
  const sortedData = [...props.data].sort((a, b) => {
    const dateA = new Date(a.tradeDate ?? a.TradeDate ?? '')
    const dateB = new Date(b.tradeDate ?? b.TradeDate ?? '')
    return dateA - dateB
  })
  
  // 保存排序后的数据引用，供tooltip使用
  currentChartData = sortedData

  // 准备K线数据 [开盘, 收盘, 最低, 最高]
  const candlestickData = sortedData.map(item => {
    const open = item.open ?? item.Open ?? 0
    const close = item.close ?? item.Close ?? 0
    const low = item.low ?? item.Low ?? 0
    const high = item.high ?? item.High ?? 0
    return [
      Number(open),
      Number(close),
      Number(low),
      Number(high)
    ]
  })

  // 准备日期数据
  const dates = sortedData.map(item => item.tradeDate ?? item.TradeDate ?? '')

  // 计算移动平均线
  const ma5 = calculateMA(sortedData, 5)
  const ma10 = calculateMA(sortedData, 10)
  const ma20 = calculateMA(sortedData, 20)
  const ma60 = calculateMA(sortedData, 60)

  // 调试信息
  if (process.env.NODE_ENV === 'development') {
    console.log('[StockChart] 更新单股票图表:', {
      dataCount: sortedData.length,
      datesCount: dates.length,
      candlestickDataCount: candlestickData.length,
      firstDate: dates[0],
      lastDate: dates[dates.length - 1],
      firstCandlestick: candlestickData[0]
    })
  }

  // 更新图表
  try {
    chartInstance.setOption({
      xAxis: {
        data: dates
      },
      series: [
        {
          name: 'K线',
          data: candlestickData
        },
        {
          name: 'MA5',
          data: ma5
        },
        {
          name: 'MA10',
          data: ma10
        },
        {
          name: 'MA20',
          data: ma20
        },
        {
          name: 'MA60',
          data: ma60
        }
      ]
    }, { notMerge: false })
  } catch (error) {
    console.error('[StockChart] 更新图表失败:', error)
  }
}

// 多股票模式更新
const updateMultiStockChart = () => {
  if (!props.multiStockData || props.multiStockData.length === 0) return

  // 为每只股票准备数据
  const stockSeries = []
  const allDates = new Set()
  const stockDataMap = new Map() // 用于存储每只股票的数据
  
  // 定义颜色数组，为每只股票分配不同颜色
  const colors = [
    { up: '#ef5350', down: '#26a69a' }, // 红色/绿色
    { up: '#ff9800', down: '#4caf50' }, // 橙色/绿色
    { up: '#2196f3', down: '#00bcd4' }, // 蓝色/青色
    { up: '#9c27b0', down: '#e91e63' }, // 紫色/粉色
    { up: '#795548', down: '#607d8b' }  // 棕色/蓝灰色
  ]

  props.multiStockData.forEach((stockInfo, index) => {
    const stockCode = stockInfo.stockCode || stockInfo.code || `股票${index + 1}`
    const stockName = stockInfo.stockName || stockInfo.name || stockCode
    const data = stockInfo.data || []
    
    if (data.length === 0) return

    // 按日期排序
    const sortedData = [...data].sort((a, b) => {
      const dateA = new Date(a.tradeDate ?? a.TradeDate ?? '')
      const dateB = new Date(b.tradeDate ?? b.TradeDate ?? '')
      return dateA - dateB
    })

    // 收集所有日期
    sortedData.forEach(item => {
      const date = item.tradeDate ?? item.TradeDate ?? ''
      if (date) allDates.add(date)
    })

    // 准备K线数据
    const candlestickData = sortedData.map(item => {
      const open = item.open ?? item.Open ?? 0
      const close = item.close ?? item.Close ?? 0
      const low = item.low ?? item.Low ?? 0
      const high = item.high ?? item.High ?? 0
      return [
        Number(open),
        Number(close),
        Number(low),
        Number(high)
      ]
    })

    // 保存数据供tooltip使用
    stockDataMap.set(stockCode, sortedData)

    // 获取颜色
    const color = colors[index % colors.length]

    // 创建K线系列
    stockSeries.push({
      name: `${stockName}(${stockCode})`,
      type: 'candlestick',
      data: candlestickData,
      xAxisIndex: 0,
      yAxisIndex: 0,
      itemStyle: {
        color: color.up,
        color0: color.down,
        borderColor: color.up,
        borderColor0: color.down
      }
    })
  })

  // 合并所有日期并排序
  const sortedDates = Array.from(allDates).sort((a, b) => {
    return new Date(a) - new Date(b)
  })

  // 更新图表
  chartInstance.setOption({
    title: {
      text: `多股票对比走势图（${props.multiStockData.length}只股票）`
    },
    legend: {
      data: stockSeries.map(s => s.name),
      top: 35,
      left: 'center'
    },
    xAxis: {
      data: sortedDates
    },
    series: stockSeries
  })

  // 保存多股票数据供tooltip使用
  currentChartData = Array.from(stockDataMap.values()).flat()
}

// 处理窗口大小变化
const handleResize = () => {
  if (chartInstance) {
    chartInstance.resize()
  }
}

// 监听数据变化
watch(() => props.data, (newData, oldData) => {
  if (process.env.NODE_ENV === 'development') {
    console.log('[StockChart] props.data变化:', {
      newLength: newData?.length || 0,
      oldLength: oldData?.length || 0,
      hasData: !!newData && newData.length > 0,
      isArray: Array.isArray(newData)
    })
  }
  nextTick(() => {
    updateChart()
  })
}, { deep: true, immediate: true })

watch(() => props.multiStockData, () => {
  nextTick(() => {
    updateChart()
  })
}, { deep: true })

watch(() => props.stockName, () => {
  if (chartInstance) {
    const useMultiStock = props.multiStockData && props.multiStockData.length > 0
    if (!useMultiStock) {
      chartInstance.setOption({
        title: {
          text: props.stockName ? `${props.stockName} - 股价走势（含主要均线）` : '股价走势（含主要均线）'
        }
      })
    }
  }
})

onMounted(() => {
  nextTick(() => {
    initChart()
    // 延迟一下确保DOM已渲染和props已传递
    setTimeout(() => {
      if (process.env.NODE_ENV === 'development') {
        console.log('[StockChart] onMounted后更新图表:', {
          hasData: !!props.data,
          dataLength: props.data?.length || 0,
          chartInstanceExists: !!chartInstance
        })
      }
      updateChart()
    }, 200)
  })
})

onBeforeUnmount(() => {
  if (chartInstance) {
    window.removeEventListener('resize', handleResize)
    chartInstance.dispose()
    chartInstance = null
  }
})
</script>

<style scoped>
.stock-chart-container {
  width: 100%;
  height: 500px;
  min-height: 400px;
}
</style>

