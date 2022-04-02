<template>
  <div>
    <div style="background: #1940b0">
      <v-avatar tile size="64" class="ma-3">
        <v-img src="@/assets/images/logo-small.png"></v-img>
      </v-avatar>
      <span
        style="font-family: 'Arial-Narow-Bold'; color: white; font-size: 23px"
        >{{ $t("appName") }}</span
      >
    </div>
    <v-divider></v-divider>
    <v-list dense class="pt-0">
      <v-list-item
        v-for="item in activeItems"
        :key="item.title"
        :to="item.link"
        active-class="my-active-class"
      >
        <v-list-item-action>
          <v-icon>{{ item.icon }}</v-icon>
        </v-list-item-action>
        <v-list-item-content>
          <v-list-item-title>{{ item.title }}</v-list-item-title>
        </v-list-item-content>
      </v-list-item>

      <v-divider></v-divider>
      <!-- News -->
      <v-list-item
        href="https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md"
        target="_blank"
      >
        <v-list-item-action>
          <v-icon>campaign</v-icon>
        </v-list-item-action>
        <v-list-item-content>
          <v-list-item-title>{{ this.$t("changelog") }}</v-list-item-title>
        </v-list-item-content>
      </v-list-item>

      <!-- Feedback -->
      <v-list-item
        href="https://docs.google.com/forms/d/e/1FAIpQLSd5AQesTSbDo23_4CkNiKmSPtPBaZIuFjAFnjqLo6XGKG5gyg/viewform?usp=sf_link"
        target="_blank"
      >
        <v-list-item-action>
          <v-icon>feedback</v-icon>
        </v-list-item-action>
        <v-list-item-content>
          <v-list-item-title>{{ this.$t("feedback") }}</v-list-item-title>
        </v-list-item-content>
      </v-list-item>

      <v-divider></v-divider>
      <v-list-item
       href="https://github.com/vpnhood/VpnHood/wiki/VpnHood-Access-Server"
        target="_blank">
        <v-list-item-action>
          <v-icon>admin_panel_settings</v-icon>
        </v-list-item-action>
        <v-list-item-content>
          <v-list-item-title>
            {{ this.$t("createServer") }}
          </v-list-item-title>
        </v-list-item-content>
      </v-list-item>

      <v-divider></v-divider>
      <v-list-item>
        <v-list-item-content>
          <v-list-item-title
            >{{ this.$t("version") }}:
            {{ this.store.appVersion() }}</v-list-item-title
          >
        </v-list-item-content>
      </v-list-item>
    </v-list>
  </div>
</template>

<script>

export default {
  data: () => {
    return {};
  },
  computed: {
    activeItems() {
      return this.store.navigationItems().filter(x => x.enabled);
    },
    darkMode:
    {
      get() {
        return this.$vuetify.theme.dark;
      },
      set(value) {
        this.store.userSettings.darkMode = value;
        this.store.saveUserSettings();
        this.store.updateLayout(this);
      }
    }
  },
  methods: {
  }
};
</script>


