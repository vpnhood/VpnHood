<template>
  <div>
    <v-toolbar flat>
      <v-list>
        <v-list-item>
          <v-list-item-avatar>
            <!-- <img src="/img/app_avatar.png" /> -->
            <v-icon>vpn_key</v-icon>
          </v-list-item-avatar>
        </v-list-item>
      </v-list>
    </v-toolbar>
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
      <v-list-item>
        <v-list-item-action>
          <v-icon>nights_stay</v-icon>
        </v-list-item-action>
        <v-list-item-content>
          {{ $t("darkMode") }}
          <v-switch v-model="darkMode" reverse class="ml-8"></v-switch>
        </v-list-item-content>
      </v-list-item>

      <v-divider></v-divider>
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


