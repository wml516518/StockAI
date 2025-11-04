<template>
  <div class="container">
    <div class="content">
      <div class="card">
        <h3>最新金融新闻</h3>
        <div class="news-controls">
          <input 
            v-model="searchKeyword" 
            type="text" 
            placeholder="搜索新闻关键词" 
            @keyup.enter="handleSearch"
            :disabled="loading"
          >
          <button class="btn" @click="handleSearch" :disabled="loading">🔍 搜索</button>
          <button class="btn" @click="resetSearch" :disabled="loading">🔄 重置</button>
        </div>
        
        <!-- 分页控制 -->
        <div class="pagination-controls" v-if="!isSearching && pagination.totalCount > 0">
          <div class="pagination-info">
            共 {{ pagination.totalCount }} 条新闻，第 {{ pagination.pageIndex }} / {{ pagination.totalPages }} 页
          </div>
          <div class="pagination-buttons">
            <select v-model.number="pagination.pageSize" @change="handlePageSizeChange" :disabled="loading">
              <option :value="5">每页 5 条</option>
              <option :value="10">每页 10 条</option>
              <option :value="50">每页 50 条</option>
            </select>
          </div>
        </div>
        
        <!-- 搜索分页控制 -->
        <div class="pagination-controls" v-if="isSearching && searchPagination.totalCount > 0">
          <div class="pagination-info">
            搜索到 {{ searchPagination.totalCount }} 条新闻，第 {{ searchPagination.pageIndex }} / {{ searchPagination.totalPages }} 页
          </div>
          <div class="pagination-buttons">
            <select v-model.number="searchPagination.pageSize" @change="handleSearchPageSizeChange" :disabled="loading">
              <option :value="5">每页 5 条</option>
              <option :value="10">每页 10 条</option>
              <option :value="50">每页 50 条</option>
            </select>
          </div>
        </div>
        
        <div v-if="loading" class="loading">
          <div>📰 正在加载新闻...</div>
        </div>
        <div v-else-if="newsList.length === 0" class="loading">暂无新闻</div>
        <div v-else class="news-list">
          <div v-for="news in newsList" :key="news.id || news.title" class="news-item">
            <div class="news-header">
              <h4 class="news-title">{{ news.title }}</h4>
              <span class="news-time">{{ formatDate(news.publishTime) }}</span>
            </div>
            <p class="news-content">{{ truncateContent(news.content, 200) }}</p>
            <div class="news-footer">
              <span class="news-source">来源: {{ news.source || '未知' }}</span>
              <a v-if="news.url" :href="news.url" target="_blank" class="news-link">查看原文</a>
            </div>
          </div>
        </div>
        
        <!-- 分页导航 -->
        <div class="pagination" v-if="!isSearching && pagination.totalCount > 0">
          <button 
            class="btn btn-small" 
            @click="goToPage(pagination.pageIndex - 1)" 
            :disabled="pagination.pageIndex <= 1 || loading"
          >
            ← 上一页
          </button>
          <span class="page-info">
            第 {{ pagination.pageIndex }} 页，共 {{ pagination.totalPages || Math.ceil(pagination.totalCount / pagination.pageSize) }} 页
            <span style="margin-left: 10px; color: #666; font-size: 0.9em;">
              (共 {{ pagination.totalCount }} 条)
            </span>
          </span>
          <button 
            class="btn btn-small" 
            @click="goToPage(pagination.pageIndex + 1)" 
            :disabled="pagination.pageIndex >= (pagination.totalPages || Math.ceil(pagination.totalCount / pagination.pageSize)) || loading"
          >
            下一页 →
          </button>
        </div>
        
        <!-- 搜索分页导航 -->
        <div class="pagination" v-if="isSearching && searchPagination.totalCount > 0">
          <button 
            class="btn btn-small" 
            @click="goToSearchPage(searchPagination.pageIndex - 1)" 
            :disabled="searchPagination.pageIndex <= 1 || loading"
          >
            ← 上一页
          </button>
          <span class="page-info">
            第 {{ searchPagination.pageIndex }} 页，共 {{ searchPagination.totalPages || Math.ceil(searchPagination.totalCount / searchPagination.pageSize) }} 页
            <span style="margin-left: 10px; color: #666; font-size: 0.9em;">
              (共 {{ searchPagination.totalCount }} 条)
            </span>
          </span>
          <button 
            class="btn btn-small" 
            @click="goToSearchPage(searchPagination.pageIndex + 1)" 
            :disabled="searchPagination.pageIndex >= (searchPagination.totalPages || Math.ceil(searchPagination.totalCount / searchPagination.pageSize)) || loading"
          >
            下一页 →
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import api from '../services/api'

const newsList = ref([])
const loading = ref(false)
const searchKeyword = ref('')
const isInitialized = ref(false)
const isSearching = ref(false)

// 分页信息
const pagination = ref({
  pageIndex: 1,
  pageSize: 10,
  totalCount: 0,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false
})

const searchPagination = ref({
  pageIndex: 1,
  pageSize: 10,
  totalCount: 0,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false
})

const fetchNews = async (pageIndex = 1, pageSize = null) => {
  console.log('📰 [前端] ========== fetchNews 开始 ==========')
  console.log('📰 [前端] 参数: pageIndex=', pageIndex, ', pageSize=', pageSize)
  console.log('📰 [前端] 当前loading状态:', loading.value)
  
  if (loading.value) {
    console.log('📰 [前端] 已在加载中，跳过请求')
    return // 防止重复请求
  }
  
  console.log('📰 [前端] 设置loading=true')
  loading.value = true
  isSearching.value = false
  
  try {
    const pageSizeToUse = pageSize || pagination.value.pageSize
    let response
    
    console.log('📰 [前端] ============================================')
    console.log('📰 [前端] 开始获取新闻: PageIndex=', pageIndex, ', PageSize=', pageSizeToUse)
    console.log('📰 [前端] ============================================')
    
    // 先尝试使用分页API
    try {
      console.log('📰 [前端] 调用分页API: /news/latest/paged')
      response = await api.get('/news/latest/paged', {
        params: { 
          pageIndex: pageIndex,
          pageSize: pageSizeToUse
        }
      })
      console.log('📰 [前端] 分页API调用成功')
    } catch (error) {
      console.error('📰 [前端] 分页API调用失败:', error)
      // 如果分页API失败（404），尝试使用旧的非分页API
      if (error.response?.status === 404 && pageIndex === 1) {
        console.log('分页API不可用，使用旧API')
        response = await api.get('/news/latest', {
          params: { count: pageSizeToUse }
        })
      } else {
        throw error
      }
    }
    
    console.log('📰 [前端] 新闻API响应:', response)
    console.log('📰 [前端] 响应类型:', typeof response)
    console.log('📰 [前端] 是否为数组:', Array.isArray(response))
    console.log('📰 [前端] 响应键:', response ? Object.keys(response) : 'null')
    
    // 处理分页响应
    if (response) {
      // 检查是否是PagedResult格式
      if (response.items !== undefined) {
        console.log('📰 [前端] 检测到PagedResult格式，items数量:', response.items?.length || 0)
        console.log('📰 [前端] 分页信息: TotalCount=', response.totalCount, ', PageIndex=', response.pageIndex, ', PageSize=', response.pageSize)
        
        const items = Array.isArray(response.items) ? response.items : []
        console.log('📰 [前端] 新闻列表详情:')
        if (items.length > 0) {
          items.slice(0, 3).forEach((news, index) => {
            console.log(`  [${index + 1}] 标题: ${news.title || '无标题'}, 发布时间: ${news.publishTime || '未知'}, 来源: ${news.source || '未知'}`)
          })
          if (items.length > 3) {
            console.log(`  ... 还有 ${items.length - 3} 条新闻`)
          }
        } else {
          console.warn('⚠️ [前端] items数组为空！')
        }
        
        newsList.value = items
        
        // 计算分页信息
        const totalCount = response.totalCount || 0
        const currentPageSize = response.pageSize || pageSizeToUse
        const currentPageIndex = response.pageIndex || pageIndex
        const calculatedTotalPages = Math.ceil(totalCount / currentPageSize)
        
        pagination.value = {
          pageIndex: currentPageIndex,
          pageSize: currentPageSize,
          totalCount: totalCount,
          totalPages: response.totalPages || calculatedTotalPages,
          hasPreviousPage: response.hasPreviousPage !== undefined ? response.hasPreviousPage : (currentPageIndex > 1),
          hasNextPage: response.hasNextPage !== undefined ? response.hasNextPage : (currentPageIndex < calculatedTotalPages)
        }
        
        console.log('📰 [前端] 设置后的新闻列表数量:', newsList.value.length)
        console.log('📰 [前端] 分页状态:', pagination.value)
        console.log('📰 [前端] 分页计算: TotalCount=', totalCount, ', PageSize=', currentPageSize, ', TotalPages=', pagination.value.totalPages)
        console.log('📰 [前端] 分页按钮状态: HasPrevious=', pagination.value.hasPreviousPage, ', HasNext=', pagination.value.hasNextPage)
      } 
      // 如果直接返回数组（兼容旧格式）
      else if (Array.isArray(response)) {
        console.log('📰 [前端] 检测到数组格式，数量:', response.length)
        if (response.length > 0) {
          console.log('📰 [前端] 数组新闻预览（前3条）:')
          response.slice(0, 3).forEach((news, index) => {
            console.log(`  [${index + 1}] 标题: ${news.title || '无标题'}, 发布时间: ${news.publishTime || '未知'}`)
          })
        }
        newsList.value = response
        pagination.value = {
          pageIndex: 1,
          pageSize: pageSizeToUse,
          totalCount: response.length,
          totalPages: Math.ceil(response.length / pageSizeToUse),
          hasPreviousPage: false,
          hasNextPage: response.length >= pageSizeToUse
        }
        console.log('📰 [前端] 设置后的新闻列表数量:', newsList.value.length)
      }
      // 如果返回空对象或其他格式
      else {
        console.warn('意外的响应格式:', response)
        newsList.value = []
        resetPagination()
      }
    } else {
      newsList.value = []
      resetPagination()
    }
  } catch (error) {
    console.error('❌ [前端] 获取新闻失败:', error)
    console.error('❌ [前端] 错误详情:', {
      message: error.message,
      response: error.response,
      status: error.response?.status,
      data: error.response?.data
    })
    console.error('❌ [前端] 错误堆栈:', error.stack)
    newsList.value = []
    resetPagination()
    console.log('📰 [前端] 已清空新闻列表和分页信息')
    // 不显示错误提示，避免干扰用户（可能是数据库中没有新闻）
  } finally {
    console.log('📰 [前端] fetchNews 完成，最终新闻列表数量:', newsList.value.length)
    loading.value = false
  }
}

const resetPagination = () => {
  pagination.value = {
    pageIndex: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false
  }
}

const resetSearchPagination = () => {
  searchPagination.value = {
    pageIndex: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false
  }
}

onMounted(() => {
  if (!isInitialized.value) {
    fetchNews()
    isInitialized.value = true
  }
})

onActivated(() => {
  if (!loading.value && !isInitialized.value) {
    fetchNews()
    isInitialized.value = true
  }
})

const handleSearch = async () => {
  if (!searchKeyword.value.trim()) {
    resetSearch()
    return
  }
  
  if (loading.value) return // 防止重复请求
  
  loading.value = true
  isSearching.value = true
  
  try {
    let response
    
    // 先尝试使用分页搜索API
    try {
      response = await api.get('/news/search/paged', {
        params: { 
          keyword: searchKeyword.value.trim(),
          pageIndex: searchPagination.value.pageIndex,
          pageSize: searchPagination.value.pageSize
        }
      })
    } catch (error) {
      // 如果分页API失败（404），尝试使用旧的非分页API
      if (error.response?.status === 404 && searchPagination.value.pageIndex === 1) {
        console.log('分页搜索API不可用，使用旧API')
        response = await api.get('/news/search', {
          params: { keyword: searchKeyword.value.trim() }
        })
      } else {
        throw error
      }
    }
    
    // 处理分页响应
    if (response) {
      // 检查是否是PagedResult格式
      if (response.items !== undefined) {
        newsList.value = Array.isArray(response.items) ? response.items : []
        searchPagination.value = {
          pageIndex: response.pageIndex || 1,
          pageSize: response.pageSize || searchPagination.value.pageSize,
          totalCount: response.totalCount || 0,
          totalPages: response.totalPages || 0,
          hasPreviousPage: response.hasPreviousPage || false,
          hasNextPage: response.hasNextPage || false
        }
      }
      // 如果直接返回数组（兼容旧格式）
      else if (Array.isArray(response)) {
        newsList.value = response
        searchPagination.value = {
          pageIndex: 1,
          pageSize: searchPagination.value.pageSize,
          totalCount: response.length,
          totalPages: Math.ceil(response.length / searchPagination.value.pageSize),
          hasPreviousPage: false,
          hasNextPage: response.length >= searchPagination.value.pageSize
        }
      }
      // 如果返回空对象或其他格式
      else {
        newsList.value = []
        resetSearchPagination()
      }
    } else {
      newsList.value = []
      resetSearchPagination()
    }
  } catch (error) {
    console.error('搜索失败:', error)
    newsList.value = []
    resetSearchPagination()
    alert('搜索失败: ' + (error.response?.data?.message || error.message))
  } finally {
    loading.value = false
  }
}

const resetSearch = () => {
  searchKeyword.value = ''
  isSearching.value = false
  resetSearchPagination()
  fetchNews(1, pagination.value.pageSize)
}

const goToPage = (pageIndex) => {
  const totalPages = pagination.value.totalPages || Math.ceil(pagination.value.totalCount / pagination.value.pageSize)
  console.log('📰 [前端] goToPage 调用: pageIndex=', pageIndex, ', totalPages=', totalPages)
  
  if (pageIndex < 1 || pageIndex > totalPages) {
    console.warn('📰 [前端] 页码无效:', pageIndex)
    return
  }
  
  console.log('📰 [前端] 跳转到第', pageIndex, '页')
  fetchNews(pageIndex)
  // 滚动到顶部
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

const goToSearchPage = (pageIndex) => {
  if (pageIndex < 1 || pageIndex > searchPagination.value.totalPages) return
  searchPagination.value.pageIndex = pageIndex
  handleSearch()
  // 滚动到顶部
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

const handlePageSizeChange = () => {
  fetchNews(1, pagination.value.pageSize)
}

const handleSearchPageSizeChange = () => {
  searchPagination.value.pageIndex = 1
  handleSearch()
}

const formatDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN')
}

const truncateContent = (content, maxLength = 200) => {
  if (!content) return ''
  if (content.length <= maxLength) return content
  return content.substring(0, maxLength) + '...'
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.news-controls {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
}

.news-controls input {
  flex: 1;
}

.pagination-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 15px;
  padding: 10px;
  background: #f5f5f5;
  border-radius: 4px;
}

.pagination-info {
  font-size: 0.9em;
  color: #666;
}

.pagination-buttons select {
  padding: 5px 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 0.9em;
}

.pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 15px;
  margin-top: 20px;
  padding: 15px;
}

.page-info {
  font-size: 0.9em;
  color: #666;
}

.btn-small {
  padding: 6px 12px;
  font-size: 0.9em;
}

.news-list {
  margin-top: 20px;
}

.news-item {
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 20px;
  margin-bottom: 15px;
  background: #f8f9fa;
  transition: all 0.3s;
}

.news-item:hover {
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
  transform: translateY(-2px);
}

.news-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  margin-bottom: 10px;
}

.news-title {
  font-size: 1.2em;
  font-weight: bold;
  color: #333;
  flex: 1;
  margin-right: 15px;
}

.news-time {
  font-size: 0.85em;
  color: #999;
  white-space: nowrap;
}

.news-content {
  color: #666;
  line-height: 1.6;
  margin-bottom: 10px;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.news-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.9em;
  color: #999;
}

.news-source {
  font-style: italic;
}

.news-link {
  color: #667eea;
  text-decoration: none;
}

.news-link:hover {
  text-decoration: underline;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .news-controls {
    flex-direction: column;
  }
  
  .news-header {
    flex-direction: column;
    gap: 5px;
  }
  
  .pagination-controls {
    flex-direction: column;
    gap: 10px;
    align-items: flex-start;
  }
  
  .pagination {
    flex-direction: column;
    gap: 10px;
  }
}
</style>
