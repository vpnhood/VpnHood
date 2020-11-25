import firebase from "firebase/app";
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