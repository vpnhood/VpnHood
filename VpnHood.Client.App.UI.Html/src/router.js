import Vue from 'vue'
import VueRouter from 'vue-router'
import Home from './pages/Home.vue'
import page_templpate from './pages/page_templpate.vue'

Vue.use(VueRouter);

export default new VueRouter({
  mode: 'history',
  base: process.env.BASE_URL,
  routes: [
    {
      path: '/',
      redirect: '/home'
    },
    {
      path: '/home',
      component: Home
    },
    {
      path: '/pagetemplpate',
      component: page_templpate
    },
    {
      path: '*',
      redirect: '/',
    }
  ]
});

