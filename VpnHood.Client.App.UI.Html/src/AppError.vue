<template>
  <v-app>
    <v-card height="100%">
      <v-alert :value="true" type="error">{{this.error}}</v-alert>
      <p>Please run the app and try again.</p>
    </v-card>
  </v-app>
</template>

<script>

export default {
  props: {
    error: Error,
  },
  created() {},
};
</script>
