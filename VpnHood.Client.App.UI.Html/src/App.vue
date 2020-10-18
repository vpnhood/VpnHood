<template>
  <v-app>
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

    <v-app-bar app>
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
    </v-app-bar>

    <v-main>
      <v-bottom-sheet v-model="errorSheet" hide-overlay dark>
        <v-sheet color="grey darken-2" class="pa-2">
          <v-icon>error_outline</v-icon>
            {{ store.state.lastError }}
        </v-sheet>
      </v-bottom-sheet>
      <router-view />
    </v-main>
  </v-app>
</template>

<script>
import Navigation from "./components/Navigation";

export default {
  name: 'App',

  components: {
    Navigation
  },

  data: () => ({
    drawer: null //null to let initailize by default for mobile and desktop
  }),
  computed: {
    errorSheet: {
      get() {
        return this.store.state.lastError != null
      },
      set: function (value) {
        if (value == false) {
          this.store.invoke("clearLastError");
        }
      }
    }
  }
};
</script>
