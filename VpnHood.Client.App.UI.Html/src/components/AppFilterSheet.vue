<template>
  <v-dialog
    inset
    v-model="sheetVisible"
    value="true"
    max-width="600"
    scrollable
    fullscreen
    hide-overlay
    transition="dialog-bottom-transition"
  >
    <v-card rounded class="">
      <v-toolbar max-height="48">
        <v-btn icon @click="sheetVisible = false">
          <v-icon small>close</v-icon>
        </v-btn>
        <v-toolbar-title class="pl-0">
          {{ $t("appFilter") }}
        </v-toolbar-title>
      </v-toolbar>

      <v-card-text>
        <v-select
          class="ma-4"
          v-model="store.userSettings.appFiltersMode"
          :items="getFilterModes()"
          :label="$t('appFilterDesc')"
          @change="configChanged()"
        ></v-select>

        <!-- autocomplete -->
        <v-autocomplete
          :loading="appsLoaded"
          v-if="store.userSettings.appFiltersMode != 'All'"
          v-model="store.userSettings.appFilters"
          :items="store.installedApps ? store.installedApps : []"
          filled
          chips
          color="blue-grey lighten-2"
          :label="$t('selectedApps')"
          item-text="appName"
          item-value="appId"
          multiple
          @change="configChanged()"
        >
          <template v-slot:selection="data">
            <v-chip
              v-bind="data.attrs"
              :input-value="data.selected"
              close
              @click="data.select"
              @click:close="updateItem(data.item.appId, false)"
            >
              <v-avatar left>
                <v-img
                  :src="'data:image/png;base64, ' + data.item.iconPng"
                ></v-img>
              </v-avatar>
              {{ data.item.appName }}
            </v-chip>
          </template>
          <template v-slot:item="data">
            <template>
              <v-list-item-avatar>
                <img :src="'data:image/png;base64, ' + data.item.iconPng" />
              </v-list-item-avatar>
              <v-list-item-content>
                <v-list-item-title
                  v-html="data.item.appName"
                ></v-list-item-title>
                <v-list-item-subtitle></v-list-item-subtitle>
              </v-list-item-content>
            </template>
          </template>
        </v-autocomplete>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>


<script>

export default {
  name: 'AppFilter',
  components: {
  },
  created() {
    this.isRouterBusy = false;
    this.refresh();
  },
  beforeDestroy() {
  },
  data() {
    return {
      appsLoaded: true,
      search: ""
    }
  },
  computed: {
    sheetVisible: {
      get() {
        return this.$route.query.appFilter != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          this.$router.back();
        }
      }
    },
  },
  watch:
  {
    "$route"() {
      this.isRouterBusy = false;
    }
  },
  methods: {
    async refresh() {
      this.appsLoaded = true;
      await this.store.loadInstalledApps();
      this.appsLoaded = false;
    },

    getFilterModes() {
      // set filter apps
      let filterModes = [{
        text: this.$t('appFilterAll'),
        value: 'All',
      }];

      if (this.store.features.isExcludeApplicationsSupported)
        filterModes.push({
          text: this.$t('appFilterExclude'),
          value: 'Exclude',
        });

      if (this.store.features.isIncludeApplicationsSupported)
        filterModes.push({
          text: this.$t('appFilterInclude'),
          value: 'Include',
        });

      return filterModes;
    },

    updateItem(appId, isChecked) {
      this.store.userSettings.appFilters = this.store.userSettings.appFilters.filter(x => x != appId);
      if (isChecked)
        this.store.userSettings.appFilters.push(appId);
      this.configChanged();
    },

    configChanged() {
      if (this.store.connectionState("$") != 'None')
        this.store.disconnect();
      this.store.saveUserSettings();
    }
  }
}
</script>
