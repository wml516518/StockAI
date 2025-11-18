import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const DEFAULT_ANALYSIS_TYPE = 'comprehensive'

const normalizeStockCode = (code) => {
  if (!code) return ''
  return code.toString().trim().toUpperCase()
}

const createSession = (stockCode = '', analysisType = DEFAULT_ANALYSIS_TYPE, stockName = '') => ({
  id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
  stockCode: normalizeStockCode(stockCode),
  analysisType: analysisType || DEFAULT_ANALYSIS_TYPE,
  displayName: stockName || '',
  analyzing: false,
  result: '',
  analysisDate: '',
  analysisTime: '',
  stockInfo: null,
  isCached: false,
  hasAnalyzed: false,
  lastAnalyzedStockCode: '',
  technicalChart: null,
  chartData: [], // 图表数据，每个session独立
  rating: null,
  actionSuggestion: null,
  chatMessages: [],
  chatVisible: false,
  chatInput: '',
  chatLoading: false,
  chatError: ''
})

export const useAiAnalysisStore = defineStore('aiAnalysis', () => {
  const sessions = ref([])
  const activeSessionId = ref('')

  const analysisTypeLabels = {
    comprehensive: '综合',
    fundamental: '基本面',
    news: '新闻',
    technical: '技术面'
  }

  const currentSession = computed(() => {
    if (!sessions.value.length) return null
    return sessions.value.find(session => session.id === activeSessionId.value) || sessions.value[0] || null
  })

  const ensureDefaultSession = () => {
    if (!sessions.value.length) {
      const session = createSession()
      sessions.value.push(session)
      activeSessionId.value = session.id
    } else if (!sessions.value.some(session => session.id === activeSessionId.value)) {
      activeSessionId.value = sessions.value[0].id
    }
  }

  const setActiveSession = (sessionId) => {
    if (!sessionId) {
      ensureDefaultSession()
      return
    }
    const exists = sessions.value.some(session => session.id === sessionId)
    if (exists) {
      activeSessionId.value = sessionId
    } else {
      ensureDefaultSession()
    }
  }

  const resetSessionState = (session, preserveCode = true) => {
    if (!session) return
    if (!preserveCode) {
      session.stockCode = ''
    }
    session.analysisType = DEFAULT_ANALYSIS_TYPE
    session.analyzing = false
    session.result = ''
    session.analysisDate = ''
    session.analysisTime = ''
    session.stockInfo = null
    session.isCached = false
    session.hasAnalyzed = false
    session.lastAnalyzedStockCode = ''
    session.technicalChart = null
    session.chartData = [] // 清空图表数据
    session.rating = null
    session.actionSuggestion = null
    session.chatMessages = []
    session.chatVisible = false
    session.chatInput = ''
    session.chatLoading = false
    session.chatError = ''
  }

  const addSession = (stockCode = '', analysisType = DEFAULT_ANALYSIS_TYPE, stockName = '') => {
    const normalizedCode = stockCode ? normalizeStockCode(stockCode) : ''
    const session = createSession(normalizedCode, analysisType, stockName)
    sessions.value.push(session)
    activeSessionId.value = session.id
    return session
  }

  const findSessionByStockCode = (stockCode) => {
    const normalized = normalizeStockCode(stockCode)
    if (!normalized) return null
    return sessions.value.find(session => normalizeStockCode(session.stockCode) === normalized) || null
  }

  const closeSession = (sessionId) => {
    if (!sessions.value.length) {
      ensureDefaultSession()
      return
    }

    if (sessions.value.length <= 1) {
      resetSessionState(sessions.value[0], false)
      activeSessionId.value = sessions.value[0].id
      return
    }

    const index = sessions.value.findIndex(session => session.id === sessionId)
    if (index === -1) {
      return
    }

    const wasActive = activeSessionId.value === sessionId
    sessions.value.splice(index, 1)

    if (wasActive) {
      const nextSession = sessions.value[index] || sessions.value[index - 1] || sessions.value[0] || null
      activeSessionId.value = nextSession ? nextSession.id : ''
      ensureDefaultSession()
    }
  }

  const upsertSession = (stockCode, analysisType = DEFAULT_ANALYSIS_TYPE, stockName = '') => {
    const normalizedCode = normalizeStockCode(stockCode)

    if (!normalizedCode) {
      ensureDefaultSession()
      return currentSession.value
    }

    let session = findSessionByStockCode(normalizedCode)
    if (!session) {
      session = addSession(normalizedCode, analysisType, stockName)
    } else {
      session.stockCode = normalizedCode
      if (analysisType && analysisType !== session.analysisType) {
        session.analysisType = analysisType
      }
    if (stockName && stockName !== session.displayName) {
      session.displayName = stockName
    }
      activeSessionId.value = session.id
    }
    return session
  }

  const removeAllSessions = () => {
    sessions.value = []
    activeSessionId.value = ''
    ensureDefaultSession()
  }

  return {
    sessions,
    activeSessionId,
    currentSession,
    analysisTypeLabels,
    ensureDefaultSession,
    setActiveSession,
    addSession,
    closeSession,
    resetSessionState,
    findSessionByStockCode,
    upsertSession,
    removeAllSessions
  }
})

export {
  DEFAULT_ANALYSIS_TYPE,
  normalizeStockCode
}

