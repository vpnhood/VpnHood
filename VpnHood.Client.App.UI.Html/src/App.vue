<template>
  <v-app>
    <AddServerSheet />
    <ClientProfileSheet />
    <ErrorSheet />
    <ServersSheet />
    <v-navigation-drawer
      app
      :width="250"
      :mobile-breakpoint="600"
      :disable-resize-watcher="false"
      :right="$vuetify.rtl"
      v-model="store.navigationDrawer"
    >
      <Navigation />
    </v-navigation-drawer>

    <!-- <v-app-bar app>
      <v-app-bar-nav-icon
        @click.stop="store.navigationDrawer = !store.navigationDrawer"
      ></v-app-bar-nav-icon>
      <v-toolbar-title>
        {{ store.title }}
      </v-toolbar-title>
      <v-spacer></v-spacer>

      <v-tooltip bottom v-for="(item, i) in store.toolbarItems" :key="i">
        <template v-slot:activator="{ on, attrs }" v-if="!item.hidden">
          <v-btn
            :disabled="item.disabled"
            icon
            @click="item.click"
            v-bind="attrs"
            v-on="on"
          >
            <v-icon>{{ item.icon }}</v-icon>
          </v-btn>
        </template>
        <span>{{ item.tooltip }}</span>
      </v-tooltip>
    </v-app-bar> -->

    <v-main>
      <router-view />
    </v-main>
  </v-app>
</template>

<style>
html {
  overflow-y: auto !important;
}
</style>

<script>
import Navigation from "./components/Navigation";
import ErrorSheet from "./components/ErrorSheet";
import AddServerSheet from "./components/AddServerSheet";
import ClientProfileSheet from "./components/ClientProfileSheet";
import ServersSheet from "./pages/Servers";

export default {
  name: 'App',

  components: {
    Navigation,
    ErrorSheet,
    AddServerSheet,
    ClientProfileSheet,
    ServersSheet
  },

  data: () => ({
    drawer: null //null to let initailize by default for mobile and desktop
  })
};
</script>
