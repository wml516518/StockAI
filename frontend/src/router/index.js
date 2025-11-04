import { createRouter, createWebHistory } from 'vue-router'
import Watchlist from '../views/Watchlist.vue'
import Screen from '../views/Screen.vue'
import QuantTrading from '../views/QuantTrading.vue'
import News from '../views/News.vue'
import AI from '../views/AI.vue'
import Alert from '../views/Alert.vue'
import Settings from '../views/Settings.vue'

const routes = [
  {
    path: '/',
    redirect: '/watchlist'
  },
  {
    path: '/watchlist',
    name: 'Watchlist',
    component: Watchlist
  },
  {
    path: '/screen',
    name: 'Screen',
    component: Screen
  },
  {
    path: '/quant',
    name: 'QuantTrading',
    component: QuantTrading
  },
  {
    path: '/news',
    name: 'News',
    component: News
  },
  {
    path: '/ai',
    name: 'AI',
    component: AI
  },
  {
    path: '/alert',
    name: 'Alert',
    component: Alert
  },
  {
    path: '/settings',
    name: 'Settings',
    component: Settings
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

export default router

