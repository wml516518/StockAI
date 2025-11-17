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

          <div
            v-if="currentSession.result"
            class="result-card"
            ref="analysisCardRef"
          >
            <div class="result-header">
              <div class="result-header-info">
                <h4>分析结果</h4>
                <div v-if="currentSession.analysisDate" class="analysis-date">
                  <span v-if="currentSession.isCached" class="cache-badge">📦 缓存数据</span>
                  📅 分析时间：{{ currentSession.analysisTime || currentSession.analysisDate }}
                  <span v-if="currentSession.stockInfo" class="stock-info">（{{ currentSession.stockInfo.name }}，当前价：{{ formatNumber(currentSession.stockInfo?.currentPrice) }}）</span>
                </div>
              </div>
              <div class="result-action-group">
                <button type="button" class="btn btn-export" @click="handleExportPdf">
                  📄 一键导出
                </button>
                <button type="button" class="btn btn-chat" @click="toggleChat">
                  {{ currentSession.chatVisible ? '⬇ 收起对话' : '💬 和AI继续对话' }}
                </button>
              </div>
            </div>

            <div v-if="chartData.length > 0" class="chart-section">
              <h5>技术面图表</h5>
              <!-- ECharts 图表 -->
              <StockChart
                :data="chartData"
                :highlights="chartHighlightsObj"
                :stock-name="currentSession.stockInfo?.name || currentSession.displayName || ''"
              />
              <ul v-if="chartHighlights.length" class="chart-highlights">
                <li v-for="item in chartHighlights" :key="item.label">
                  <strong>{{ item.label }}：</strong>{{ item.value }}
                </li>
              </ul>
            </div>

            <div class="analysis-content">{{ currentSession.result }}</div>

            <div
              v-if="currentSession.chatVisible"
              class="chat-panel"
              ref="chatPanel"
            >
              <div class="chat-panel-header">
                <div class="chat-panel-title">AI 对话</div>
                <div class="chat-panel-actions">
                  <button
                    type="button"
                    class="chat-panel-action"
                    @click="clearChatHistory"
                    :disabled="currentSession.chatLoading || !currentSession.chatMessages.length"
                    title="清空当前对话"
                  >
                    🗑
                  </button>
                  <button
                    type="button"
                    class="chat-panel-action"
                    @click="closeChat"
                    title="收起聊天"
                  >
                    ×
                  </button>
                </div>
              </div>

              <div class="chat-status" v-if="currentSession.chatLoading">AI 正在思考，请稍等...</div>
              <div
                class="chat-status"
                v-else-if="!currentSession.chatMessages.length"
              >
                与AI继续对话，系统会保留最近 {{ MAX_CHAT_ROUNDS }} 轮记录。
              </div>

              <div class="chat-messages" ref="chatMessagesContainer">
                <div
                  v-for="(message, index) in currentSession.chatMessages"
                  :key="index"
                  :class="['chat-message', message.role]"
                >
                  <div class="chat-message-role">
                    {{ message.role === 'user' ? '我' : 'AI' }}
                  </div>
                  <div class="chat-bubble">
                    {{ message.content }}
                  </div>
                </div>
              </div>

              <div v-if="currentSession.chatError" class="chat-error">
                {{ currentSession.chatError }}
              </div>

              <form class="chat-input-row" @submit.prevent="handleSendChatMessage">
                <textarea
                  ref="chatTextarea"
                  v-model="currentSession.chatInput"
                  class="chat-textarea"
                  :placeholder="currentSession.chatLoading ? 'AI 正在回复...' : '向AI提问，按Enter发送，Shift+Enter换行'"
                  :disabled="currentSession.chatLoading"
                  @keydown="handleChatTextareaKeydown"
                ></textarea>
                <div class="chat-actions">
                  <button type="submit" class="btn" :disabled="!canSendChat">发送</button>
                </div>
              </form>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, onActivated, watch, computed, ref, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { storeToRefs } from 'pinia'
import api from '../services/api'
import { stockService } from '../services/stockService'
import { useAiAnalysisStore, normalizeStockCode } from '../stores/aiAnalysis'
import { useWatchlistStore } from '../stores/watchlist'
import StockChart from '../components/StockChart.vue'

const route = useRoute()
const aiAnalysisStore = useAiAnalysisStore()
const watchlistStore = useWatchlistStore()
const { sessions, activeSessionId, currentSession } = storeToRefs(aiAnalysisStore)
const { analysisTypeLabels } = aiAnalysisStore

const MAX_CHAT_ROUNDS = 5
const chatMessagesContainer = ref(null)
const chatPanel = ref(null)
const chatTextarea = ref(null)
const analysisCardRef = ref(null)
let jsPdfLoaderPromise = null
let html2CanvasLoaderPromise = null

const canSendChat = computed(() => {
  const session = currentSession.value
  if (!session) {
    return false
  }
  return Boolean(session.chatInput && session.chatInput.trim() && !session.chatLoading)
})

const getJsPdf = async () => {
  if (typeof window === 'undefined') return null
  if (window.jspdf?.jsPDF) {
    return window.jspdf.jsPDF
  }
  if (!jsPdfLoaderPromise) {
    jsPdfLoaderPromise = new Promise((resolve, reject) => {
      const script = document.createElement('script')
      script.src = 'https://cdn.jsdelivr.net/npm/jspdf@2.5.1/dist/jspdf.umd.min.js'
      script.async = true
      script.onload = () => {
        if (window.jspdf?.jsPDF) {
          resolve(window.jspdf.jsPDF)
        } else {
          jsPdfLoaderPromise = null
          reject(new Error('未能加载 jsPDF 模块'))
        }
      }
      script.onerror = () => {
        jsPdfLoaderPromise = null
        reject(new Error('下载 jsPDF 失败'))
      }
      document.body.appendChild(script)
    })
  }
  return jsPdfLoaderPromise
}

const getHtml2Canvas = async () => {
  if (typeof window === 'undefined') return null
  if (window.html2canvas) {
    return window.html2canvas
  }
  if (!html2CanvasLoaderPromise) {
    html2CanvasLoaderPromise = new Promise((resolve, reject) => {
      const script = document.createElement('script')
      script.src = 'https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js'
      script.async = true
      script.onload = () => {
        if (window.html2canvas) {
          resolve(window.html2canvas)
        } else {
          html2CanvasLoaderPromise = null
          reject(new Error('未能加载 html2canvas 模块'))
        }
      }
      script.onerror = () => {
        html2CanvasLoaderPromise = null
        reject(new Error('下载 html2canvas 失败'))
      }
      document.body.appendChild(script)
    })
  }
  return html2CanvasLoaderPromise
}

// 图表数据
const chartData = ref([])

const chartHighlightsObj = computed(() => {
  return currentSession.value?.technicalChart?.highlights || {}
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
  chartData.value = [] // 清空图表数据

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

      // 获取历史数据用于图表显示
      try {
        const endDate = new Date()
        const startDate = new Date()
        startDate.setMonth(startDate.getMonth() - 3) // 获取最近3个月的数据
        
        const historyData = await stockService.getHistory(code, startDate.toISOString().split('T')[0], endDate.toISOString().split('T')[0])
        if (historyData && Array.isArray(historyData) && historyData.length > 0) {
          chartData.value = historyData.map(item => ({
            tradeDate: item.tradeDate,
            open: item.open,
            high: item.high,
            low: item.low,
            close: item.close,
            volume: item.volume
          }))
          console.log('已加载历史数据用于图表:', chartData.value.length, '条')
        }
      } catch (error) {
        console.warn('获取历史数据失败，图表将使用图片模式:', error)
        chartData.value = []
      }
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
    chartData.value = [] // 清空图表数据
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

const scrollChatToBottom = () => {
  if (chatMessagesContainer.value) {
    chatMessagesContainer.value.scrollTop = chatMessagesContainer.value.scrollHeight
  }
}

const toggleChat = () => {
  if (!currentSession.value) return
  currentSession.value.chatVisible = !currentSession.value.chatVisible
  if (currentSession.value.chatVisible) {
    nextTick(() => {
      if (chatPanel.value?.scrollIntoView) {
        chatPanel.value.scrollIntoView({ behavior: 'smooth', block: 'end' })
      }
      scrollChatToBottom()
      chatTextarea.value?.focus()
    })
  }
}

const closeChat = () => {
  if (!currentSession.value) return
  currentSession.value.chatVisible = false
}

const clearChatHistory = () => {
  if (!currentSession.value) return
  currentSession.value.chatMessages = []
  currentSession.value.chatError = ''
  currentSession.value.chatInput = ''
}

const sanitizeFileName = (name) => {
  if (!name) return 'AI分析报告'
  return name.replace(/[\\/:*?"<>|]/g, '_').replace(/\s+/g, ' ').trim() || 'AI分析报告'
}

const handleExportPdf = async () => {
  const session = currentSession.value
  if (!session || !session.result) {
    alert('暂无可导出的分析内容，请先完成AI分析。')
    return
  }

  let previousDisplay = null
  let analysisElement = null
  try {
    const [jsPDF, html2canvas] = await Promise.all([getJsPdf(), getHtml2Canvas()])
    if (!jsPDF || !html2canvas) {
      alert('PDF 导出模块加载失败，请检查网络后重试。')
      return
    }

    analysisElement = analysisCardRef.value
    if (!analysisElement) {
      alert('未找到可导出的内容区域，请刷新后重试。')
      return
    }

    await nextTick()

    const scale = Math.max(window.devicePixelRatio || 1, 2)

    const actionGroup = analysisElement.querySelector('.result-action-group')
    if (actionGroup) {
      previousDisplay = actionGroup.style.display
      actionGroup.style.display = 'none'
    }

    const canvas = await html2canvas(analysisElement, {
      scale,
      useCORS: true,
      backgroundColor: '#ffffff',
      scrollY: -window.scrollY
    })

    const imageData = canvas.toDataURL('image/png', 1.0)
    const doc = new jsPDF({
      unit: 'pt',
      format: 'a4'
    })

    const pageWidth = doc.internal.pageSize.getWidth()
    const pageHeight = doc.internal.pageSize.getHeight()

    const imgWidth = pageWidth
    const imgHeight = (canvas.height * imgWidth) / canvas.width

    let heightLeft = imgHeight
    let position = 0

    const stockName = session.stockInfo?.name || session.displayName || session.stockCode || 'AI分析'
    const fileName = `${sanitizeFileName(stockName)}.pdf`

    doc.addImage(imageData, 'PNG', 0, position, imgWidth, imgHeight, undefined, 'FAST')
    heightLeft -= pageHeight

    while (heightLeft > 0) {
      position = heightLeft - imgHeight
      doc.addPage()
      doc.addImage(imageData, 'PNG', 0, position, imgWidth, imgHeight, undefined, 'FAST')
      heightLeft -= pageHeight
    }

    doc.save(fileName)
  } catch (error) {
    console.error('导出PDF失败:', error)
    alert('导出PDF失败，请稍后再试。')
  } finally {
    if (analysisElement) {
      const actionGroup = analysisElement.querySelector('.result-action-group')
      if (actionGroup) {
        actionGroup.style.display = previousDisplay ?? ''
      }
    }
  }
}

const handleChatTextareaKeydown = (event) => {
  if (
    event.key === 'Enter' &&
    !event.shiftKey &&
    !event.ctrlKey &&
    !event.altKey &&
    !event.metaKey
  ) {
    event.preventDefault()
    handleSendChatMessage()
  }
}

const handleSendChatMessage = async () => {
  const session = currentSession.value
  if (!session || session.chatLoading) {
    return
  }

  const content = session.chatInput?.trim()
  if (!content) {
    return
  }

  if (!Array.isArray(session.chatMessages)) {
    session.chatMessages = []
  }

  const isFirstTurn = session.chatMessages.length === 0

  const userMessage = {
    role: 'user',
    content
  }

  session.chatMessages.push(userMessage)
  if (session.chatMessages.length > MAX_CHAT_ROUNDS * 2) {
    session.chatMessages.splice(0, session.chatMessages.length - MAX_CHAT_ROUNDS * 2)
  }

  const payloadMessages = session.chatMessages.map(msg => ({
    role: msg.role,
    content: msg.content
  }))

  session.chatInput = ''
  session.chatLoading = true
  session.chatError = ''

  try {
    const payload = {
      stockCode: session.stockCode,
      analysisType: session.analysisType,
      analysisTypeLabel: analysisTypeLabels[session.analysisType] || analysisTypeLabels.comprehensive,
      analysisSummary: isFirstTurn ? session.result : undefined,
      includeAnalysisContext: isFirstTurn,
      messages: payloadMessages,
      maxHistory: MAX_CHAT_ROUNDS,
      // 总是获取实时数据，即使不是第一次对话
      forceRealTimeData: true
    }

    const response = await api.post('/ai/chat', payload, { timeout: 120000 })

    let replyText = ''
    if (response && typeof response === 'object') {
      replyText = response.reply || response.message || ''
    } else if (typeof response === 'string') {
      replyText = response
    }

    if (!replyText) {
      replyText = 'AI没有返回有效回复，请稍后再试。'
    }

    session.chatMessages.push({
      role: 'assistant',
      content: replyText
    })
    if (session.chatMessages.length > MAX_CHAT_ROUNDS * 2) {
      session.chatMessages.splice(0, session.chatMessages.length - MAX_CHAT_ROUNDS * 2)
    }

    await nextTick()
    scrollChatToBottom()
  } catch (error) {
    console.error('AI聊天失败:', error)

    const lastIndex = session.chatMessages.length - 1
    if (
      lastIndex >= 0 &&
      session.chatMessages[lastIndex].role === 'user' &&
      session.chatMessages[lastIndex].content === userMessage.content
    ) {
      session.chatMessages.splice(lastIndex, 1)
    }

    const errorData = error.response?.data
    let message = ''
    if (errorData) {
      if (typeof errorData === 'string') {
        message = errorData
      } else if (typeof errorData === 'object') {
        message = errorData.reply || errorData.message || errorData.error || ''
      }
    }
    if (!message && error.message) {
      message = error.message
    }
    session.chatInput = content
    session.chatError = message || '聊天失败，请稍后重试。'
  } finally {
    session.chatLoading = false
  }
}

watch(
  () => currentSession.value?.chatMessages?.length,
  async () => {
    if (!currentSession.value?.chatVisible) {
      return
    }
    await nextTick()
    if (chatPanel.value?.scrollIntoView) {
      chatPanel.value.scrollIntoView({ behavior: 'smooth', block: 'end' })
    }
    scrollChatToBottom()
  }
)

watch(
  () => currentSession.value?.chatVisible,
  async (visible) => {
    if (visible) {
      await nextTick()
      if (chatPanel.value?.scrollIntoView) {
        chatPanel.value.scrollIntoView({ behavior: 'smooth', block: 'end' })
      }
      scrollChatToBottom()
      chatTextarea.value?.focus()
    }
  }
)

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
  margin: 0;
  color: #667eea;
}

.result-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 15px;
  padding-bottom: 15px;
  border-bottom: 1px solid #e0e0e0;
}

.result-header-info {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.result-action-group {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  justify-content: flex-end;
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

.btn-chat {
  background: #f1f4ff;
  color: #4650dd;
  border: 1px solid #cdd5ff;
  border-radius: 6px;
  padding: 6px 14px;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.btn-chat:hover {
  background: #e2e7ff;
  box-shadow: 0 2px 8px rgba(102, 126, 234, 0.25);
}

.btn-chat:disabled {
  opacity: 0.6;
  cursor: not-allowed;
  box-shadow: none;
}

.btn-export {
  background: #e8f8f0;
  color: #21865d;
  border: 1px solid #9ee0c4;
  border-radius: 6px;
  padding: 6px 14px;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.btn-export:hover {
  background: #d4f0e3;
  box-shadow: 0 2px 8px rgba(33, 134, 93, 0.2);
}

.btn-export:disabled {
  opacity: 0.6;
  cursor: not-allowed;
  box-shadow: none;
}

.analysis-content {
  white-space: pre-wrap;
  line-height: 1.6;
  color: #333;
  word-break: break-word;
}

.chat-panel {
  margin-top: 20px;
  border: 1px solid #dce1ff;
  border-radius: 10px;
  background: #fafbff;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.chat-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.chat-panel-title {
  font-weight: 600;
  color: #1f3c88;
}

.chat-panel-actions {
  display: flex;
  gap: 6px;
}

.chat-panel-action {
  background: transparent;
  border: none;
  font-size: 18px;
  cursor: pointer;
  color: #7c88b5;
  line-height: 1;
  padding: 2px;
}

.chat-panel-action:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.chat-panel-action:hover:not(:disabled) {
  color: #1f3c88;
}

.chat-status {
  font-size: 13px;
  color: #6c757d;
}

.chat-messages {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-right: 4px;
  max-height: 500px;
  overflow-y: auto;
}

.chat-message {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.chat-message.user {
  align-items: flex-end;
}

.chat-message.assistant {
  align-items: flex-start;
}

.chat-message-role {
  font-size: 12px;
  color: #7c88b5;
}

.chat-message.user .chat-message-role {
  color: #4650dd;
}

.chat-bubble {
  max-width: 100%;
  padding: 10px 14px;
  border-radius: 12px;
  background: #fff;
  color: #2f3b52;
  box-shadow: 0 2px 8px rgba(31, 60, 136, 0.08);
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
}

.chat-message.user .chat-bubble {
  background: #667eea;
  color: #fff;
  box-shadow: 0 4px 12px rgba(102, 126, 234, 0.25);
}

.chat-error {
  color: #dc3545;
  font-size: 13px;
}

.chat-input-row {
  display: flex;
  gap: 10px;
  align-items: flex-end;
}

.chat-textarea {
  flex: 1;
  min-height: 90px;
  padding: 10px;
  border: 1px solid #ccd4ff;
  border-radius: 8px;
  font-size: 14px;
  resize: vertical;
}

.chat-textarea:focus {
  outline: none;
  border-color: #667eea;
  box-shadow: 0 0 0 2px rgba(102, 126, 234, 0.15);
}

.chat-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.chat-actions .btn {
  white-space: nowrap;
  padding: 10px 18px;
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

  .chat-panel {
    padding: 12px;
  }

  .chat-messages {
    max-height: 220px;
  }

  .chat-input-row {
    flex-direction: column;
    align-items: stretch;
  }

  .chat-actions {
    flex-direction: row;
    justify-content: flex-end;
  }

  .result-action-group {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>

