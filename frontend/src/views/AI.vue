<template>
  <div class="container">
    <div class="content">
      <div class="card">
        <h3>AI股票分析</h3>

        <div class="session-tabs">
          <div class="tab-list">
            <button
              v-for="session in sessions"
              :key="session.id"
              :class="['session-tab', { active: session.id === activeSessionId }]"
              @click="setActiveSession(session.id)"
            >
              <span class="tab-label">
                {{ getSessionLabel(session) }}
                <span v-if="session.analyzing" class="tab-loading-dot"></span>
              </span>
              <span
                v-if="sessions.length > 1"
                class="tab-close"
                @click.stop="closeSession(session.id)"
                title="关闭分析页签"
              >
                ×
              </span>
            </button>
            <button class="add-session-tab" @click="handleAddSession" title="新增分析页签">
              ＋ 新分析
            </button>
          </div>
        </div>

        <div v-if="currentSession" class="session-body">
          <div class="form-group">
            <label>股票代码</label>
            <input v-model="currentSession.stockCode" type="text" placeholder="输入要分析的股票代码">
          </div>
          <div class="form-group">
            <label>分析类型</label>
            <select v-model="currentSession.analysisType" class="form-control">
              <option value="comprehensive">综合分析</option>
              <option value="fundamental">基本面分析</option>
              <option value="news">新闻舆论分析</option>
              <option value="technical">技术面分析</option>
            </select>
          </div>
          <div class="actions">
            <button class="btn" @click="handleAnalyzeCurrent()" :disabled="currentSession.analyzing">开始分析</button>
            <button
              v-if="currentSession.isCached"
              class="btn btn-secondary"
              @click="handleRefreshAnalysis"
              :disabled="currentSession.analyzing"
            >
              🔄 重新分析
            </button>
          </div>

          <div v-if="currentSession.analyzing" class="loading-state">
            <div class="loading-spinner"></div>
            <p>AI正在分析中，请稍候...</p>
          </div>

          <div v-if="currentSession.result" class="result-card">
            <div class="result-header">
              <h4>分析结果</h4>
              <div v-if="currentSession.analysisDate" class="analysis-date">
                <span v-if="currentSession.isCached" class="cache-badge">📦 缓存数据</span>
                📅 分析时间：{{ currentSession.analysisTime || currentSession.analysisDate }}
                <span v-if="currentSession.stockInfo" class="stock-info">（{{ currentSession.stockInfo.name }}，当前价：{{ formatNumber(currentSession.stockInfo?.currentPrice) }}）</span>
              </div>
            </div>

            <div v-if="chartImageSrc" class="chart-section">
              <h5>技术面图表</h5>
              <img :src="chartImageSrc" alt="股价走势图" class="chart-image" />
              <ul v-if="chartHighlights.length" class="chart-highlights">
                <li v-for="item in chartHighlights" :key="item.label">
                  <strong>{{ item.label }}：</strong>{{ item.value }}
                </li>
              </ul>
            </div>

            <div class="analysis-content">{{ currentSession.result }}</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, onActivated, watch, computed } from 'vue'
import { useRoute } from 'vue-router'
import { storeToRefs } from 'pinia'
import api from '../services/api'
import { stockService } from '../services/stockService'
import { useAiAnalysisStore, normalizeStockCode } from '../stores/aiAnalysis'
import { useWatchlistStore } from '../stores/watchlist'

const route = useRoute()
const aiAnalysisStore = useAiAnalysisStore()
const watchlistStore = useWatchlistStore()
const { sessions, activeSessionId, currentSession } = storeToRefs(aiAnalysisStore)
const { analysisTypeLabels } = aiAnalysisStore

const chartImageSrc = computed(() => {
  const chart = currentSession.value?.technicalChart
  if (!chart?.imageBase64) {
    return ''
  }
  const contentType = chart.contentType || 'image/png'
  return `data:${contentType};base64,${chart.imageBase64}`
})

const formatNumber = (value, digits = 2) => {
  const num = Number(value)
  if (!isFinite(num)) {
    return 'N/A'
  }
  return num.toFixed(digits)
}

const formatPercent = (value, digits = 2) => {
  const formatted = formatNumber(value, digits)
  return formatted === 'N/A' ? 'N/A' : `${formatted}%`
}

const chartHighlights = computed(() => {
  const highlights = currentSession.value?.technicalChart?.highlights
  if (!highlights || typeof highlights !== 'object') {
    return []
  }

  const items = []

  if (highlights.highest) {
    const { price, date } = highlights.highest
    items.push({
      label: '最高价',
      value: `${formatNumber(price)}（${date || '未知日期'}）`
    })
  }

  if (highlights.lowest) {
    const { price, date } = highlights.lowest
    items.push({
      label: '最低价',
      value: `${formatNumber(price)}（${date || '未知日期'}）`
    })
  }

  if (highlights.latest) {
    const { price, date } = highlights.latest
    items.push({
      label: '当前价',
      value: `${formatNumber(price)}（${date || '未知日期'}）`
    })
  }

  if (highlights.movingAverages && typeof highlights.movingAverages === 'object') {
    const maTexts = Object.entries(highlights.movingAverages)
      .map(([key, value]) => `${key}: ${formatNumber(value)}`)
      .join(' / ')
    if (maTexts) {
      items.push({
        label: '均线（最新）',
        value: maTexts
      })
    }
  }

  if (highlights.period) {
    const { startDate, endDate, startPrice, endPrice, changePercent } = highlights.period
    items.push({
      label: '区间表现',
      value: `${startDate || ''} → ${endDate || ''}，${formatNumber(startPrice)} → ${formatNumber(endPrice)}（${formatPercent(changePercent)}）`
    })
  }

  return items
})

const formatDate = (date) => {
  if (!date) return ''

  if (typeof date === 'string' && /^\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}/.test(date)) {
    return date.replace('T', ' ').substring(0, 19)
  }

  const d = new Date(date)

  if (isNaN(d.getTime())) {
    console.warn('无效的日期值:', date)
    return ''
  }

  const year = d.getFullYear()
  if (year < 1900 || year === 1) {
    console.warn('检测到无效的默认日期值，使用当前时间:', date, '年份:', year)
    const now = new Date()
    const nowYear = now.getFullYear()
    const nowMonth = String(now.getMonth() + 1).padStart(2, '0')
    const nowDay = String(now.getDate()).padStart(2, '0')
    const nowHours = String(now.getHours()).padStart(2, '0')
    const nowMinutes = String(now.getMinutes()).padStart(2, '0')
    const nowSeconds = String(now.getSeconds()).padStart(2, '0')
    return `${nowYear}-${nowMonth}-${nowDay} ${nowHours}:${nowMinutes}:${nowSeconds}`
  }

  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  const hours = String(d.getHours()).padStart(2, '0')
  const minutes = String(d.getMinutes()).padStart(2, '0')
  const seconds = String(d.getSeconds()).padStart(2, '0')

  return `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`
}

const getSessionLabel = (session) => {
  if (!session) return '新分析'
  const name = session.stockInfo?.name || session.displayName?.trim()
  const code = session.stockCode?.trim()
  const typeLabel = analysisTypeLabels[session.analysisType] || analysisTypeLabels.comprehensive
  const base = name || code || '新分析'
  return `${base}（${typeLabel}）`
}

const setActiveSession = (sessionId) => {
  aiAnalysisStore.setActiveSession(sessionId)
}

const handleAddSession = () => {
  aiAnalysisStore.addSession()
}

const closeSession = (sessionId) => {
  aiAnalysisStore.closeSession(sessionId)
}

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

const handleAnalyze = async (session, forceRefresh = false) => {
  if (!session) {
    return
  }

  if (!session.stockCode?.trim()) {
    alert('请输入股票代码')
    return
  }

  if (session.analyzing) {
    console.log('分析正在进行中，跳过重复调用')
    return
  }

  const code = normalizeStockCode(session.stockCode)
  session.stockCode = code
  session.analyzing = true
  session.result = ''
  session.analysisDate = ''
  session.analysisTime = ''
  session.stockInfo = null
  session.isCached = false
  session.technicalChart = null
  session.rating = null
  session.actionSuggestion = null

  try {
    console.log('正在获取股票最新数据...', code)

    let stockData = null
    let dataDate = null

    try {
      stockData = await stockService.getStock(code)
      if (stockData) {
        let lastUpdateValue = stockData.lastUpdate
        if (lastUpdateValue) {
          const testDate = new Date(lastUpdateValue)
          if (isNaN(testDate.getTime()) || testDate.getFullYear() < 1900 || testDate.getFullYear() === 1) {
            console.warn('股票数据的lastUpdate无效，使用当前时间:', lastUpdateValue)
            lastUpdateValue = new Date().toISOString()
          }
        } else {
          lastUpdateValue = new Date().toISOString()
        }

        dataDate = lastUpdateValue
        session.stockInfo = {
          name: stockData.name,
          currentPrice: stockData.currentPrice,
          changePercent: stockData.changePercent,
          pe: stockData.pe,
          pb: stockData.pb
        }
        session.analysisDate = formatDate(dataDate)
        console.log('获取到股票数据:', stockData.name, '更新时间:', dataDate)
        if (stockData.name && stockData.name !== session.displayName) {
          session.displayName = stockData.name
        }
      } else {
        dataDate = new Date().toISOString()
        session.analysisDate = formatDate(dataDate)
      }
    } catch (error) {
      console.warn('获取股票数据失败，将使用当前时间:', error)
      dataDate = new Date().toISOString()
      session.analysisDate = formatDate(dataDate)
    }

    const context = getAnalysisContext(session.analysisType, stockData, dataDate)

    console.log('开始调用AI分析接口...', { forceRefresh, analysisType: session.analysisType })
    const response = await api.post(`/ai/analyze/${code}`, {
      context: context,
      analysisType: session.analysisType,
      forceRefresh: forceRefresh
    }, {
      timeout: 600000
    })

    console.log('AI分析响应:', response)
    console.log('响应类型:', typeof response)

    if (response && typeof response === 'object') {
      if (response.analysis) {
        session.result = response.analysis
      } else if (response.result) {
        session.result = response.result
      } else if (response.message) {
        session.result = response.message
      } else if (typeof response === 'string') {
        session.result = response
      } else {
        session.result = JSON.stringify(response, null, 2)
      }

      session.isCached = response.cached === true
      if (response.analysisTime) {
        session.analysisTime = response.analysisTime
      } else if (response.timestamp) {
        session.analysisTime = response.timestamp
      }

      session.technicalChart = response.technicalChart || null
      session.rating = response.rating || null
      session.actionSuggestion = response.actionSuggestion || null

      console.log('AI分析结果已设置，长度:', session.result?.length || 0, '是否缓存:', session.isCached, '分析时间:', session.analysisTime)

      session.hasAnalyzed = true
      session.lastAnalyzedStockCode = code

      watchlistStore.setStockRecommendation(code, session.rating, session.actionSuggestion)
    } else if (typeof response === 'string') {
      session.result = response
      session.technicalChart = null
      watchlistStore.setStockRecommendation(code, session.rating, session.actionSuggestion)
    } else {
      session.result = '分析完成，但响应格式异常'
      session.technicalChart = null
      watchlistStore.setStockRecommendation(code, session.rating, session.actionSuggestion)
    }
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
      session.result = '分析超时: AI分析时间过长（已设置10分钟超时），请稍后重试或检查AI服务配置'
    } else if (error.message?.includes('Network Error') || error.code === 'ERR_NETWORK') {
      session.result = '网络错误: 无法连接到后端服务。请检查：\n1. 后端服务是否正常运行\n2. 网络连接是否正常\n3. 查看浏览器控制台获取详细错误信息'
    } else if (error.response) {
      const status = error.response.status
      const data = error.response.data

      let errorMessage = '未知错误'
      if (data) {
        if (typeof data === 'string') {
          errorMessage = data
        } else if (data.message) {
          errorMessage = data.message
        } else if (data.error) {
          errorMessage = data.error
        } else if (data.title) {
          errorMessage = data.title
        } else {
          try {
            errorMessage = JSON.stringify(data, null, 2)
          } catch {
            errorMessage = String(data)
          }
        }
      } else if (error.message) {
        errorMessage = error.message
      }

      session.result = `分析失败 (HTTP ${status}): ${errorMessage}`

      console.error('完整错误响应:', {
        status,
        data: error.response.data,
        headers: error.response.headers
      })
    } else {
      session.result = '分析失败: ' + (error.message || '未知错误')
    }
    session.technicalChart = null
  } finally {
    session.analyzing = false
    if (!session.result || session.result.includes('失败') || session.result.includes('错误')) {
      session.hasAnalyzed = false
    }
  }
}

const handleAnalyzeCurrent = (forceRefresh = false) => {
  if (!currentSession.value) return
  handleAnalyze(currentSession.value, forceRefresh)
}

const handleRefreshAnalysis = () => {
  handleAnalyzeCurrent(true)
}

const upsertSessionFromRoute = () => {
  const stockCode = route.query.stockCode
  const analysisType = route.query.analysisType

  if (!stockCode) {
    aiAnalysisStore.ensureDefaultSession()
    return
  }

  const stockName = route.query.stockName
  const session = aiAnalysisStore.upsertSession(stockCode, analysisType, stockName)
  const normalizedCode = normalizeStockCode(stockCode)

  if (session && !session.analyzing) {
    const shouldAnalyze = !session.hasAnalyzed || session.lastAnalyzedStockCode !== normalizedCode
    if (shouldAnalyze) {
      handleAnalyze(session, false)
    }
  }
}

watch(
  () => route.query.stockCode,
  (newStockCode, oldStockCode) => {
    if (newStockCode === oldStockCode) {
      return
    }
    upsertSessionFromRoute()
  }
)

onMounted(() => {
  if (sessions.value.length === 0) {
    aiAnalysisStore.ensureDefaultSession()
  }
  upsertSessionFromRoute()
})

onActivated(() => {
  if (sessions.value.length === 0) {
    aiAnalysisStore.ensureDefaultSession()
  } else if (!currentSession.value) {
    aiAnalysisStore.ensureDefaultSession()
  }
  upsertSessionFromRoute()
})
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

.chart-section {
  margin-bottom: 20px;
  padding: 16px;
  border: 1px solid #d8e2ff;
  border-radius: 6px;
  background: #f4f7ff;
}

.chart-section h5 {
  margin-bottom: 12px;
  color: #1f3c88;
  font-size: 16px;
  font-weight: 600;
}

.chart-image {
  width: 100%;
  max-height: 320px;
  object-fit: contain;
  background: #fff;
  border: 1px solid #e1e6f8;
  border-radius: 4px;
  padding: 8px;
  box-shadow: 0 2px 6px rgba(31, 60, 136, 0.08);
  margin-bottom: 12px;
}

.chart-highlights {
  list-style: none;
  padding: 0;
  margin: 0;
}

.chart-highlights li {
  font-size: 14px;
  color: #2f3b52;
  margin-bottom: 6px;
}

.chart-highlights li strong {
  color: #1f3c88;
  font-weight: 600;
}

.btn-secondary {
  background-color: #6c757d;
}

.btn:disabled {
  background-color: #ccc;
  cursor: not-allowed;
}

.cache-badge {
  background-color: #17a2b8;
  color: white;
  padding: 2px 8px;
  border-radius: 3px;
  font-size: 12px;
  margin-right: 8px;
}

.session-tabs {
  margin-bottom: 20px;
}

.tab-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.session-tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  border: 1px solid #d0d5ff;
  border-radius: 6px;
  background: #f5f7ff;
  color: #1f3c88;
  cursor: pointer;
  transition: all 0.2s ease;
}

.session-tab:hover {
  background: #e5e9ff;
}

.session-tab.active {
  background: #667eea;
  color: #fff;
  border-color: #667eea;
  box-shadow: 0 4px 10px rgba(102, 126, 234, 0.25);
}

.tab-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tab-close {
  margin-left: 4px;
  font-weight: bold;
  cursor: pointer;
  color: inherit;
}

.tab-close:hover {
  opacity: 0.8;
}

.add-session-tab {
  padding: 6px 12px;
  border: 1px dashed #99a3ff;
  border-radius: 6px;
  background: transparent;
  color: #5a6ded;
  cursor: pointer;
  transition: all 0.2s ease;
}

.add-session-tab:hover {
  background: #eef1ff;
}

.tab-loading-dot {
  width: 8px;
  height: 8px;
  background-color: currentColor;
  border-radius: 50%;
  animation: tab-blink 1s ease-in-out infinite;
}

@keyframes tab-blink {
  0%, 100% { opacity: 0.3; }
  50% { opacity: 1; }
}

.actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 10px;
}

.session-body {
  margin-top: 10px;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }

  .tab-list {
    flex-direction: column;
    align-items: stretch;
  }

  .session-tab,
  .add-session-tab {
    width: 100%;
    justify-content: space-between;
  }
}
</style>

