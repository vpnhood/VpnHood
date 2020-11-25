import Vue from 'vue';
import vuetify from './plugins/vuetify';
import VuetifyConfirm from 'vuetify-confirm';
import mixin from './plugins/mixin';
import i18n from './i18n';
import router from './router';
import store from './store';
import App from './App.vue';
import AppError from './AppError';
import 'material-design-icons-iconfont/dist/material-design-icons.css';
import '@mdi/font/css/materialdesignicons.css';

Vue.config.productionTip = false;
Vue.mixin(mixin);
Vue.use(VuetifyConfirm, { vuetify });


import firebase from "firebase/app";
import "firebase/auth";
import "firebase/storage";

var firebaseConfig = {
  apiKey: "AIzaSyB2Br41jN32DmXyH-HqdcsOXVnaGON1ay0",
  authDomain: "client-d2460.firebaseapp.com",
  databaseURL: "https://client-d2460.firebaseio.com",
  projectId: "client-d2460",
  storageBucket: "client-d2460.appspot.com",
  messagingSenderId: "216585339900",
  appId: "1:216585339900:web:17299300c94bfddc172879",
  measurementId: "G-8JZG8V0NXM"
};

// Initialize Firebase
firebase.initializeApp(firebaseConfig);

// firebase.auth().signInAnonymously()
//   .then(() => {
//     // Signed in..
//     console.log("OK");
//   })
//   .catch((error) => {
//     var errorCode = error.code;
//     var errorMessage = error.message;
//     console.log(errorCode);
//     console.log(errorMessage);
//     // ...
//   })

// Create a root reference
var storageRef = firebase.storage().ref();
var spaceRef = storageRef.child('logs/client/space3.txt');

var message = 'This is my message.';
spaceRef.putString(message).then(function(snapshot) {
   console.log('Uploaded a raw string!');
   console.log(snapshot);
 });

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

    //check for new version without waiting
    store.checkNewVersion();

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
