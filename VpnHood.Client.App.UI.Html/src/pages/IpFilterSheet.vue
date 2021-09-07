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
          {{ $t("ipFilter") }}
        </v-toolbar-title>
      </v-toolbar>

      <v-card-text>
        <v-select
          class="ma-4"
          v-model="store.userSettings.ipGroupFiltersMode"
          :items="getFilterModes()"
          :label="$t('ipFilterDesc')"
          @change="configChanged()"
        ></v-select>

        <!-- autocomplete -->
        <v-autocomplete
          :loading="groupsLoaded"
          v-if="store.userSettings.ipGroupFiltersMode != 'All'"
          v-model="store.userSettings.ipGroupFilters"
          :items="store.ipGroups ? store.ipGroups : []"
          filled
          chips
          clearable
          color="blue-grey lighten-2"
          :label="$t('selectedIpGroups')"
          item-text="ipGroupName"
          item-value="ipGroupId"
          multiple
          @change="configChanged()"
        >
          <template v-slot:selection="data">
            <v-chip
              v-bind="data.attrs"
              :input-value="data.selected"
              close
              @click="data.select"
              @click:close="updateItem(data.item.ipGroupId, false)"
            >
              <v-img
                :src="getIpGroupImageUrl(data.item)"
                max-width="24"
                class="ma-1"
              />
              {{ data.item.ipGroupName }}
            </v-chip>
          </template>
          <template v-slot:item="data">
            <template>
              <v-img
                :src="getIpGroupImageUrl(data.item)"
                max-width="24"
                class="ma-1"
              />
              <v-list-item-content>
                <v-list-item-title
                  v-html="data.item.ipGroupName"
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
  name: 'IpFilter',
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
      groupsLoaded: true,
      search: ""
    }
  },
  computed: {
    sheetVisible: {
      get() {
        return this.$route.query.ipfilter != null;
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
      this.groupsLoaded = true;
      await this.store.loadIpGroups();
      this.groupsLoaded = false;
    },

    getFilterModes() {
      // set filter apps
      let filterModes = [{
        text: this.$t('ipFilterAll'),
        value: 'All',
      }];

      filterModes.push({
        text: this.$t('ipFilterExclude'),
        value: 'Exclude',
      });

      filterModes.push({
        text: this.$t('ipFilterInclude'),
        value: 'Include',
      });

      return filterModes;
    },

    updateItem(groupId, isChecked) {
      this.store.userSettings.ipGroupFilters = this.store.userSettings.ipGroupFilters.filter(x => x != groupId);
      if (isChecked)
        this.store.userSettings.ipGroupFilters.push(groupId);
      this.configChanged();
    },

    configChanged() {
      if (this.store.connectionState("$") != 'None')
        this.store.disconnect();
      this.store.saveUserSettings();
    },

    getIpGroupImageUrl(ipGroup) {
      try {
        if (ipGroup.ipGroupId.toLowerCase() == "custom")
          return require(`@/assets/images/custom_flag.png`);
        return require(`@/assets/images/country_flags/${ipGroup.ipGroupId}.png`);
      }
      catch
      {
        return null;
      }
    }
  }
}
</script>
