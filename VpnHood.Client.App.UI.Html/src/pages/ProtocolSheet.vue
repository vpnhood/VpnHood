<template>
  <v-dialog
    v-model="sheetVisible"
    value="true"
    @click:outside="close()"
    :transition="isMobileSize ? 'dialog-bottom-transition' : ''"
    :max-width="isMobileSize ? '' : 600"
  >
    <v-card v-if="sheetVisible">
      <v-card-title class="headline grey lighten-2">
        {{ $t("protocol") }}
      </v-card-title>
      <v-radio-group v-model="useUdpChannel">
        <v-card-text>{{$t('protocol_desc')}}
        </v-card-text>
        <v-card-text>
          <v-radio :label="$t('protocol_udpOn')" :value="true"></v-radio>
          <v-radio :label="$t('protocol_udpOff')" :value="false"> </v-radio>
        </v-card-text>
      </v-radio-group>
      <v-card-actions>
        <v-spacer></v-spacer>
        <v-btn color="blue darken-1" text @click="close()">
          {{ $t("save") }}
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
          ///this.$router.back(); oncancel is already handled
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
