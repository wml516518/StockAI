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
  }
})

const chartContainer = ref(null)
let chartInstance = null

// 计算移动平均线
const calculateMA = (data, period) => {
  const result = []
  for (let i = 0; i < data.length; i++) {
    if (i < period - 1) {
      result.push(null)
    } else {
      let sum = 0
      for (let j = i - period + 1; j <= i; j++) {
        sum += data[j].close
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
          if (param.seriesName === 'K线' && Array.isArray(param.data)) {
            const data = param.data
            // ECharts candlestick 数据格式: [开盘, 收盘, 最低, 最高]
            const open = data[0] !== null && data[0] !== undefined ? Number(data[0]).toFixed(2) : 'N/A'
            const close = data[1] !== null && data[1] !== undefined ? Number(data[1]).toFixed(2) : 'N/A'
            const low = data[2] !== null && data[2] !== undefined ? Number(data[2]).toFixed(2) : 'N/A'
            const high = data[3] !== null && data[3] !== undefined ? Number(data[3]).toFixed(2) : 'N/A'
            
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
            const value = param.value !== null && param.value !== undefined ? Number(param.value).toFixed(2) : 'N/A'
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
  if (!chartInstance || !props.data || props.data.length === 0) return

  // 按日期排序
  const sortedData = [...props.data].sort((a, b) => {
    const dateA = new Date(a.tradeDate)
    const dateB = new Date(b.tradeDate)
    return dateA - dateB
  })

  // 准备K线数据 [开盘, 收盘, 最低, 最高]
  const candlestickData = sortedData.map(item => [
    Number(item.open),
    Number(item.close),
    Number(item.low),
    Number(item.high)
  ])

  // 准备日期数据
  const dates = sortedData.map(item => item.tradeDate)

  // 计算移动平均线
  const ma5 = calculateMA(sortedData, 5)
  const ma10 = calculateMA(sortedData, 10)
  const ma20 = calculateMA(sortedData, 20)
  const ma60 = calculateMA(sortedData, 60)

  // 更新图表
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
  })
}

// 处理窗口大小变化
const handleResize = () => {
  if (chartInstance) {
    chartInstance.resize()
  }
}

// 监听数据变化
watch(() => props.data, () => {
  nextTick(() => {
    updateChart()
  })
}, { deep: true })

watch(() => props.stockName, () => {
  if (chartInstance) {
    chartInstance.setOption({
      title: {
        text: props.stockName ? `${props.stockName} - 股价走势（含主要均线）` : '股价走势（含主要均线）'
      }
    })
  }
})

onMounted(() => {
  nextTick(() => {
    initChart()
    updateChart()
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

