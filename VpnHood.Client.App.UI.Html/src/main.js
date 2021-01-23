import Vue from 'vue';
import vuetify from './plugins/vuetify';
import VuetifyConfirm from 'vuetify-confirm';
import mixin from './plugins/mixin';
import i18n from './i18n';
import router from './router';
import store from './store';
import App from './App.vue';
import AppError from './AppError';
import './plugins/firebase';
import 'material-design-icons-iconfont/dist/material-design-icons.css';
import '@mdi/font/css/materialdesignicons.css';

Vue.config.productionTip = false;
Vue.mixin(mixin);
Vue.use(VuetifyConfirm, { vuetify });

// main
async function main() {
  try {
    // init app
    await store.loadApp();

    // init vue
    var vm = new Vue({
      data: {
        gStore: store
      },
      i18n,
      router,
      vuetify,
      render: h => h(App)
    }).$mount('#app')

    // Update layout
    store.updateLayout(vm);

    // mount
    vm.$mount('#app');
  }
  catch (ex) {
    // show error page
    new Vue({
      i18n,
      router,
      vuetify,
      render: h => h(AppError, { props: { error: ex } })
    }).$mount('#app');
  }
}

main();
