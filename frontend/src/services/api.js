import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  timeout: 30000, // 默认30秒，特定请求可以覆盖
  headers: {
    'Content-Type': 'application/json'
  },
  // 增加响应大小限制（默认无限制，但某些代理可能有限制）
  maxContentLength: Infinity,
  maxBodyLength: Infinity
})

// 请求拦截器
api.interceptors.request.use(
  config => {
    return config
  },
  error => {
    return Promise.reject(error)
  }
)

// 响应拦截器
api.interceptors.response.use(
  response => {
    return response.data
  },
  error => {
    console.error('API Error:', error)
    console.error('错误代码:', error.code)
    console.error('错误消息:', error.message)
    console.error('响应状态:', error.response?.status)
    console.error('响应数据:', error.response?.data)
    
    // 处理连接错误
    if (error.code === 'ECONNREFUSED' || error.message?.includes('ECONNREFUSED')) {
      console.error('❌ 无法连接到后端API服务')
      console.error('💡 请确保后端服务已启动:')
      console.error('   1. 运行 start-all-services.bat 启动所有服务')
      console.error('   2. 或手动运行: cd src/StockAnalyse.Api && dotnet run')
      console.error('   3. 后端服务应运行在 http://localhost:5000')
      
      // 显示用户友好的错误消息
      if (typeof window !== 'undefined' && window.alert) {
        alert('无法连接到后端API服务\n\n请确保后端服务已启动：\n1. 运行 start-all-services.bat\n2. 或手动启动后端服务\n\n后端地址: http://localhost:5000')
      }
    }
    
    // 处理网络错误
    if (error.code === 'ERR_NETWORK' || error.message?.includes('Network Error')) {
      console.error('❌ 网络错误')
      console.error('💡 可能的原因:')
      console.error('   1. 后端服务未启动')
      console.error('   2. 代理配置错误')
      console.error('   3. CORS问题')
      console.error('   4. 响应太大导致超时')
    }
    
    // 处理超时错误
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      console.error('❌ 请求超时')
      console.error('💡 可能的原因:')
      console.error('   1. 服务器处理时间过长')
      console.error('   2. 网络连接不稳定')
      console.error('   3. 数据量太大')
    }
    
    return Promise.reject(error)
  }
)

export default api

