<template>
  <div class="container">
    <div class="content">
      <div class="card">
        <h3>AI股票分析</h3>
        <div class="form-group">
          <label>股票代码</label>
          <input v-model="stockCode" type="text" placeholder="输入要分析的股票代码">
        </div>
        <div class="form-group">
          <label>分析类型</label>
          <select v-model="analysisType" class="form-control">
            <option value="comprehensive">综合分析</option>
            <option value="fundamental">基本面分析</option>
            <option value="news">新闻舆论分析</option>
            <option value="technical">技术面分析</option>
          </select>
        </div>
        <button class="btn" @click="handleAnalyze" :disabled="analyzing">开始分析</button>
        
        <div v-if="analyzing" class="loading-state">
          <div class="loading-spinner"></div>
          <p>AI正在分析中，请稍候...</p>
        </div>
        
        <div v-if="result" class="result-card">
          <div class="result-header">
            <h4>分析结果</h4>
            <div v-if="analysisDate" class="analysis-date">
              📅 基于 {{ analysisDate }} 的数据分析
              <span v-if="stockInfo" class="stock-info">（{{ stockInfo.name }}，当前价：{{ stockInfo.currentPrice?.toFixed(2) || 'N/A' }}）</span>
            </div>
          </div>
          <div class="analysis-content">{{ result }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import { useRoute } from 'vue-router'
import api from '../services/api'
import { stockService } from '../services/stockService'

const route = useRoute()
const stockCode = ref('')
const analysisType = ref('comprehensive')
const analyzing = ref(false)
const result = ref('')
const analysisDate = ref('')
const stockInfo = ref(null)

// 从路由参数获取股票代码
onMounted(() => {
  if (route.query.stockCode) {
    stockCode.value = route.query.stockCode
    handleAnalyze()
  }
})

onActivated(() => {
  if (route.query.stockCode) {
    stockCode.value = route.query.stockCode
    handleAnalyze()
  }
})

// 格式化日期
const formatDate = (date) => {
  if (!date) return ''
  const d = new Date(date)
  return d.toLocaleString('zh-CN', { 
    year: 'numeric', 
    month: '2-digit', 
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })
}

// 根据分析类型生成上下文描述
const getAnalysisContext = (type, stockData = null, dataDate = null) => {
  const dateInfo = dataDate ? `\n\n**重要提示**：本次分析基于 ${formatDate(dataDate)} 的最新数据。` : ''
  const stockInfo = stockData ? `\n\n**股票基本信息**：\n- 股票名称：${stockData.name || '未知'}\n- 当前价格：${stockData.currentPrice || 'N/A'}\n- 涨跌幅：${stockData.changePercent || 0}%\n- 市盈率(PE)：${stockData.pe || 'N/A'}\n- 市净率(PB)：${stockData.pb || 'N/A'}\n` : ''
  
  const contexts = {
    fundamental: `请重点从以下基本面维度进行分析：${dateInfo}${stockInfo}
1. **财务数据**：营收、净利润、ROE、资产负债率等财务指标
2. **盈利能力**：毛利率、净利率、盈利能力趋势
3. **成长性**：营收增长率、净利润增长率、成长潜力
4. **估值水平**：PE、PB、PS等估值指标，是否合理
5. **行业地位**：在所属行业中的竞争地位和市场份额
6. **风险因素**：财务风险、经营风险、行业风险等

请提供详细的基本面分析，重点关注财务健康度和投资价值。`,
    
    news: `请重点从以下新闻舆论维度进行分析：${dateInfo}${stockInfo}
1. **最新新闻**：与该股票相关的最新新闻和消息
2. **市场情绪**：新闻反映的市场情绪和投资者预期
3. **重大事件**：公司重大事件、政策影响、行业动态
4. **舆论导向**：媒体报道、分析师观点、市场讨论
5. **热点题材**：是否涉及热点概念或题材
6. **风险提示**：负面消息、潜在风险、不利因素

请结合最新新闻和舆论环境，分析对股价的潜在影响。`,
    
    technical: `请重点从以下技术面维度进行分析：${dateInfo}${stockInfo}
1. **价格趋势**：当前价格走势、支撑位、阻力位
2. **技术指标**：MA、MACD、RSI、KDJ等主要技术指标
3. **成交量**：成交量变化、量价关系
4. **形态分析**：K线形态、技术图形、突破信号
5. **买卖信号**：技术买入信号、卖出信号
6. **短期走势**：短期、中期、长期趋势判断

请提供详细的技术分析，重点关注买卖时机和价格目标位。`,
    
    comprehensive: `请进行综合分析，涵盖以下所有维度：${dateInfo}${stockInfo}
1. **基本面**：财务数据、盈利能力、成长性、估值、行业地位
2. **技术面**：价格趋势、技术指标、成交量、形态分析
3. **新闻面**：最新新闻、市场情绪、重大事件、舆论导向
4. **风险提示**：各类风险因素的综合评估
5. **投资建议**：基于全面分析的买入、持有或卖出建议

请提供全面的综合分析报告，给出明确的投资建议和风险提示。`
  }
  
  return contexts[type] || contexts.comprehensive
}

const handleAnalyze = async () => {
  if (!stockCode.value.trim()) {
    alert('请输入股票代码')
    return
  }
  
  analyzing.value = true
  result.value = ''
  analysisDate.value = ''
  stockInfo.value = null
  
  try {
    // 先获取股票最新数据，用于获取分析日期
    const code = stockCode.value.trim().toUpperCase()
    console.log('正在获取股票最新数据...', code)
    
    let stockData = null
    let dataDate = null
    
    try {
      stockData = await stockService.getStock(code)
      if (stockData) {
        dataDate = stockData.lastUpdate || new Date().toISOString()
        stockInfo.value = {
          name: stockData.name,
          currentPrice: stockData.currentPrice,
          changePercent: stockData.changePercent,
          pe: stockData.pe,
          pb: stockData.pb
        }
        analysisDate.value = formatDate(dataDate)
        console.log('获取到股票数据:', stockData.name, '更新时间:', dataDate)
      }
    } catch (error) {
      console.warn('获取股票数据失败，将使用当前时间:', error)
      dataDate = new Date().toISOString()
      analysisDate.value = formatDate(dataDate)
    }
    
    // 生成分析上下文，包含股票数据和日期信息
    const context = getAnalysisContext(analysisType.value, stockData, dataDate)
    
    // 后端接口路径是 /api/ai/analyze/{stockCode}
    // AI分析可能需要较长时间，设置超时时间为10分钟
    console.log('开始调用AI分析接口...')
    const response = await api.post(`/ai/analyze/${code}`, {
      context: context
    }, {
      timeout: 600000 // 10分钟 = 600000毫秒（AI分析可能包含大量数据）
    })
    
    console.log('AI分析响应:', response)
    console.log('响应类型:', typeof response)
    
    // 后端现在返回JSON对象 { success: true, analysis: "...", length: xxx }
    if (response && typeof response === 'object') {
      // 优先使用analysis字段
      if (response.analysis) {
        result.value = response.analysis
      } else if (response.result) {
        result.value = response.result
      } else if (response.message) {
        result.value = response.message
      } else if (typeof response === 'string') {
        // 如果整个响应是字符串（旧格式兼容）
        result.value = response
      } else {
        // 其他情况，尝试转换为字符串
        result.value = JSON.stringify(response, null, 2)
      }
    } else if (typeof response === 'string') {
      // 如果后端直接返回字符串（向后兼容）
      result.value = response
    } else {
      result.value = '分析完成，但响应格式异常'
    }
    
    console.log('AI分析结果已设置，长度:', result.value?.length || 0)
  } catch (error) {
    console.error('AI分析失败:', error)
    console.error('错误详情:', {
      code: error.code,
      message: error.message,
      response: error.response,
      status: error.response?.status,
      data: error.response?.data
    })
    
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      result.value = '分析超时: AI分析时间过长（已设置10分钟超时），请稍后重试或检查AI服务配置'
    } else if (error.message?.includes('Network Error') || error.code === 'ERR_NETWORK') {
      result.value = '网络错误: 无法连接到后端服务。请检查：\n1. 后端服务是否正常运行\n2. 网络连接是否正常\n3. 查看浏览器控制台获取详细错误信息'
    } else if (error.response) {
      // HTTP错误响应
      const status = error.response.status
      const data = error.response.data
      result.value = `分析失败 (HTTP ${status}): ${data?.message || data || error.message || '未知错误'}`
    } else {
      result.value = '分析失败: ' + (error.message || '未知错误')
    }
  } finally {
    analyzing.value = false
  }
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  font-weight: 500;
  color: #333;
}

.form-group input,
.form-group select {
  width: 100%;
  padding: 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 14px;
  box-sizing: border-box;
}

.form-group select {
  cursor: pointer;
  background-color: white;
}

.form-group select:focus,
.form-group input:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 2px rgba(102, 126, 234, 0.1);
}

.loading-state {
  text-align: center;
  padding: 40px;
}

.loading-spinner {
  display: inline-block;
  width: 40px;
  height: 40px;
  border: 4px solid rgba(0, 0, 0, 0.1);
  border-radius: 50%;
  border-top-color: #667eea;
  animation: spin 1s ease-in-out infinite;
  margin-bottom: 10px;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.result-card {
  margin-top: 20px;
  padding: 20px;
  background: #f8f9fa;
  border-radius: 8px;
  border-left: 4px solid #667eea;
}

.result-card h4 {
  margin-bottom: 15px;
  color: #667eea;
}

.result-header {
  margin-bottom: 15px;
  padding-bottom: 15px;
  border-bottom: 1px solid #e0e0e0;
}

.analysis-date {
  font-size: 0.9em;
  color: #666;
  margin-top: 8px;
}

.stock-info {
  margin-left: 10px;
  color: #667eea;
  font-weight: 500;
}

.analysis-content {
  white-space: pre-wrap;
  line-height: 1.6;
  color: #333;
  word-break: break-word;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
}
</style>

