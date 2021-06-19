<template>
  <v-dialog
    v-model="sheetVisible"
    value="true"
    @click:outside="close()"
    :max-width="isMobileSize ? '' : 600"
  >
    <v-card>
      <v-toolbar class="headline grey lighten-2">
        <v-btn icon @click="close()">
          <v-icon small>close</v-icon>
        </v-btn>
        <v-toolbar-title>{{ $t("protocol") }}</v-toolbar-title>
      </v-toolbar>
      <v-radio-group v-model="useUdpChannel" v-on:change="close()">
        <v-card-text>{{ $t("protocol_desc") }} </v-card-text>
        <v-card-text>
          <v-radio :label="$t('protocol_udpOn')" :value="true" class="my-4"></v-radio>
          <v-radio :label="$t('protocol_udpOff')" :value="false" class="my-4"> </v-radio>
        </v-card-text>
      </v-radio-group>
      <v-card-actions>
        <v-spacer></v-spacer>
        <v-btn color="blue darken-1" text @click="close()">
          {{ $t("close") }}
        </v-btn>
      </v-card-actions>
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
    useUdpChannel:
    {
      get() {
        return this.store.userSettings.useUdpChannel;
      },
      set(value) {
        this.store.userSettings.useUdpChannel = value;
        this.store.saveUserSettings();
      }
    },

    sheetVisible: {
      get() {
        return this.$route.query.protocol != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          ///this.$router.back(); oncancel is already handled in dialog
        }
      }
    },

    isMobileSize() { return this.$vuetify.breakpoint.smAndDown; },
  },

  methods: {

    async close() {
      this.$router.back();
    },

  }
}
</script>
