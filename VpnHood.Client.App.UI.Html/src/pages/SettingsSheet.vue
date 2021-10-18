<template>
  <v-dialog
    v-model="sheetVisible"
    value="true"
    @click:outside="close()"
    :max-width="isMobileSize ? '' : 600"
    fullscreen
    hide-overlay
    transition="dialog-bottom-transition"
  >
    <v-card>
      <v-toolbar dark color="primary">
        <v-btn icon dark @click="sheetVisible = false">
          <v-icon>mdi-close</v-icon>
        </v-btn>
        <v-toolbar-title>{{ $t("settings") }}</v-toolbar-title>
        <v-spacer></v-spacer>
        <v-toolbar-items>
          <v-btn dark text @click="sheetVisible = false">
            {{ $t("close") }}
          </v-btn>
        </v-toolbar-items>
      </v-toolbar>
      <v-list three-line subheader>
        <v-subheader>{{ $t("general") }}</v-subheader>
        <v-list-item>
          <v-list-item-action>
            <v-checkbox v-model="excludeLocalNetwork"></v-checkbox>
          </v-list-item-action>
          <v-list-item-content>
            <v-list-item-title>{{
              $t("excludeLocalNetwork")
            }}</v-list-item-title>
            <p class="subtitle-2 font-weight-thin mt-2">
              {{ $t("excludeLocalNetworkDesc") }}
            </p>
          </v-list-item-content>
        </v-list-item>
      </v-list>
    </v-card>
  </v-dialog>
</template>

<script>
export default {
  components: {
  },
  props: {
  },
  created() {
    this.isRouterBusy = false;
  },
  mounted() {
  },
  data: () => ({
  }),
  watch:
  {
    "$route"() {
      this.isRouterBusy = false;
    }
  },
  computed: {
    sheetVisible: {
      get() {
        return this.$route.query.settings != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          this.$router.back();
        }
      }
    },

    excludeLocalNetwork:
    {
      get() {
        return this.store.userSettings.excludeLocalNetwork;
      },
      set(value) {
        if (this.store.userSettings.excludeLocalNetwork != value) {
          this.store.userSettings.excludeLocalNetwork = value;
          this.store.saveUserSettings();
          this.store.disconnect();
        }
      }
    },

    isMobileSize() { return this.$vuetify.breakpoint.smAndDown; },
  },

  methods: {

    async close() {
      this.sheetVisible = false;
    },

  }
}
</script>