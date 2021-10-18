<template>
  <div id="sectionWrapper">
    <v-container
      id="sectionWrapperBkgnd"
      fill-height
      fluid
      :class="`px-4 pt-4 px-sm-8 pt-sm-5 state-${connectionState.toLowerCase()}`"
    >
      <v-row class="align-self-start">
        <!-- Add New Server Hint -->
        <v-snackbar
          top
          app
          color="success"
          centered
          v-model="store.newServerAdded"
          >{{ $t("newServerAdded") }}</v-snackbar
        >

        <!-- Public Server Hint -->
        <v-dialog
          :value="store.requestedPublicServerProfileId != null"
          width="500"
        >
          <v-card>
            <v-card-title
              class="headline grey lighten-2"
              v-html="$t('publicServerWarningTitle')"
            />
            <v-card-text v-html="$t('publicServerWarning')" class="pt-4" />

            <v-divider></v-divider>
            <v-card-actions>
              <v-spacer></v-spacer>
              <v-btn
                color="primary"
                text
                @click="
                  store.connect(store.requestedPublicServerProfileId, true)
                "
                v-text="$t('ok')"
              />
            </v-card-actions>
          </v-card>
        </v-dialog>

        <!-- AppBar -->
        <v-app-bar color="transparent" dark elevation="0">
          <v-app-bar-nav-icon
            @click.stop="store.navigationDrawer = !store.navigationDrawer"
          />
          <v-img
            v-if="0"
            class="mx-2"
            src="@/assets/images/logo-small.png"
            :alt="$t('appName')"
            max-height="20"
            max-width="20"
          ></v-img>
          <v-toolbar-title class="app-title">{{
            $t("appName")
          }}</v-toolbar-title>
          <v-spacer></v-spacer>
          <ClientProfileMenu
            clientProfileId="$"
            color="white"
            :showSettingsItem="true"
            :showAddServerItem="true"
            :showDeleteItem="false"
            :showRenameItem="false"
          />
        </v-app-bar>
      </v-row>

      <!-- Speed -->
      <v-row id="speedSection" class="py-0 mt-5">
        <v-col cols="6" class="py-0 my-0 text-right">
          <span class="speedLabel">{{ $t("downloadSpeed") }}:</span>
          <span class="speedValue">{{
            this.formatSpeed(this.store.state.receiveSpeed)
          }}</span>
          <span class="speedUnit">Mbps</span>
        </v-col>
        <v-col cols="6" class="py-0 my-0">
          <span class="speedLabel">{{ $t("uploadSpeed") }}:</span>
          <span class="speedValue">{{
            this.formatSpeed(this.store.state.sendSpeed)
          }}</span>
          <span class="speedUnit">Mbps</span>
        </v-col>
      </v-row>

      <!-- Circles -->
      <v-row
        id="middleSection"
        :class="`state-${connectionState.toLowerCase()} align-self-center`"
      >
        <v-col cols="12" class="ma-0 pa-0" align="center">
          <div id="circleOuter" class="mb-8">
            <div id="circle">
              <div id="circleContent" class="align-center">
                <span id="stateText">{{ store.connectionStateText("$") }}</span>

                <!-- usage -->
                <div
                  v-if="connectionState == 'Connected' && this.bandwidthUsage()"
                >
                  <div id="bandwidthUsage">
                    <span>{{ this.bandwidthUsage().used }} of</span>
                  </div>
                  <div id="bandwithTotal" v-if="connectionState == 'Connected'">
                    <span>{{ this.bandwidthUsage().total }}</span>
                  </div>
                </div>

                <!-- check -->
                <v-icon
                  class="state-icon"
                  v-if="stateIcon!=null"
                  size="90"
                  color="white"
                  >{{this.stateIcon}}</v-icon
                >
              </div>
            </div>
          </div>

          <!-- Connect Button -->
          <v-btn
            v-if="connectionState == 'None'"
            id="connectButton"
            class="main-button"
            @click="store.connect('$')"
          >
            {{ $t("connect") }}
          </v-btn>

          <!-- Diconnect Button -->
          <v-btn
            v-if="
              connectionState == 'Waiting' ||
              connectionState == 'Connecting' ||
              connectionState == 'Connected' ||
              connectionState == 'Diagnosing'
            "
            id="disconnectButton"
            class="main-button"
            @click="store.disconnect()"
          >
            <span>{{ $t("disconnect") }}</span>
          </v-btn>

          <!-- Diconnecting -->
          <v-btn
            v-if="connectionState == 'Disconnecting'"
            id="disconnectingButton"
            class="main-button"
            style="pointer-events: none"
          >
            <span>{{ $t("disconnecting") }}</span>
          </v-btn>
        </v-col>
      </v-row>

      <!-- Config -->
      <v-row id="configSection" class="align-self-end">
        <!-- *** ipFilter *** -->
        <v-col cols="12" class="py-1">
          <v-icon class="config-icon" @click="showIpFilterSheet()"
            >public</v-icon
          >
          <span class="config-label" @click="showIpFilterSheet()">{{
            $t("ipFilterStatus_title")
          }}</span>
          <v-icon class="config-arrow" flat @click="showIpFilterSheet()"
            >keyboard_arrow_right</v-icon
          >
          <span class="config" @click="showIpFilterSheet()">
            {{ this.ipFilterStatus }}</span
          >
        </v-col>

        <!-- *** appFilter *** -->
        <v-col
          cols="12"
          class="py-1"
          v-if="
            store.features.isExcludeAppsSupported ||
            store.features.isIncludeAppsSupported
          "
        >
          <v-icon class="config-icon" @click="showAppFilterSheet()"
            >apps</v-icon
          >
          <span class="config-label" @click="showAppFilterSheet()">{{
            $t("appFilterStatus_title")
          }}</span>
          <v-icon class="config-arrow" flat @click="showAppFilterSheet()"
            >keyboard_arrow_right</v-icon
          >
          <span class="config" @click="showAppFilterSheet()">
            {{ this.appFilterStatus }}</span
          >
        </v-col>

        <!-- *** Protocol *** -->
        <v-col cols="12" class="py-1">
          <v-icon class="config-icon" @click="showProtocolSheet()"
            >settings_ethernet</v-icon
          >
          <span class="config-label" @click="showProtocolSheet()">{{
            $t("protocol_title")
          }}</span>
          <v-icon class="config-arrow" flat @click="showProtocolSheet()"
            >keyboard_arrow_right</v-icon
          >
          <span class="config" @click="showProtocolSheet()">
            {{ protocolStatus }}</span
          >
        </v-col>

        <!-- *** server *** -->
        <v-col cols="12" class="py-1">
          <v-icon class="config-icon" @click="showServersSheet()">dns</v-icon>
          <span class="config-label" @click="showServersSheet()">{{
            $t("selectedServer")
          }}</span>
          <v-icon class="config-arrow" flat @click="showServersSheet()"
            >keyboard_arrow_right</v-icon
          >
          <span class="config" @click="showServersSheet()">
            {{ store.clientProfile.name("$") }}</span
          >
        </v-col>
      </v-row>
    </v-container>
  </div>
  <!-- rootContaier -->
</template>

<style>
@import "../assets/styles/custom.css";

.v-input--checkbox .v-label {
  font-size: 12px;
}
</style>

<script>
import ClientProfileMenu from "../components/ClientProfileMenu";

export default {
  name: "HomePage",
  components: {
    ClientProfileMenu
  },
  created() {
    this.store.setTitle(this.$t("home"));
    this.monitorId = setInterval(() => {
      if (!document.hidden)
        this.store.updateState();
    }, 1000);

  },
  beforeDestroy() {
    clearInterval(this.monitorId);
    this.monitorId = 0;
  },
  data: () => ({
  }),
  computed: {
    connectionState() { return this.store.connectionState("$"); },
    appFilterStatus() {
      let appFilters = this.store.userSettings.appFilters;
      if (!appFilters) appFilters = [];

      if (this.store.userSettings.appFiltersMode == 'Exclude') return this.$t("appFilterStatus_exclude", { x: appFilters.length });
      if (this.store.userSettings.appFiltersMode == 'Include') return this.$t("appFilterStatus_include", { x: appFilters.length });
      return this.$t("appFilterStatus_all");
    },
    ipFilterStatus() {
      let ipGroupFilters = this.store.userSettings.ipGroupFilters;
      if (!ipGroupFilters) ipGroupFilters = [];

      if (this.store.userSettings.ipGroupFiltersMode == 'Exclude') return this.$t("ipFilterStatus_exclude", { x: ipGroupFilters.length });
      if (this.store.userSettings.ipGroupFiltersMode == 'Include') return this.$t("ipFilterStatus_include", { x: ipGroupFilters.length });
      return this.$t("ipFilterStatus_all");
    },
    protocolStatus() {
      return (this.store.userSettings.useUdpChannel) ? this.$t('protocol_udpOn') : this.$t('protocol_udpOff');
    },
    stateIcon()
    {
      if (this.connectionState == 'Connected' && !this.bandwidthUsage()) return "check";
      if (this.connectionState == 'None') return "power_off";
      if (this.connectionState == 'Connecting') return "power";
      if (this.connectionState == 'Diagnosing') return "network_check";
      if (this.connectionState == 'Waiting') return "hourglass_top";
      return null;
    }
  },
  methods: {
    async remove(clientProfileId) {
      clientProfileId = this.store.clientProfile.updateId(clientProfileId);
      const res = await this.$confirm(this.$t("confirmRemoveServer"), { title: this.$t("warning") })
      if (res) {
        await this.store.invoke("removeClientProfile", { clientProfileId });
        this.store.loadApp();
      }
    },

    editClientProfile(clientProfileId) {
      window.gtag('event', "editprofile");
      clientProfileId = this.store.clientProfile.updateId(clientProfileId);
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, editprofile: clientProfileId } })
    },

    showAddServerSheet() {
      window.gtag('event', "addServerButton");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, addserver: '1' } })
    },

    showServersSheet() {
      window.gtag('event', "changeServer");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, servers: '1' } })
    },

    showProtocolSheet() {
      window.gtag('event', "changeProtocol");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, protocol: '1' } })
    },

    showAppFilterSheet() {
      window.gtag('event', "changeAppFilter");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, appfilter: '1' } })
    },

    showIpFilterSheet() {
      window.gtag('event', "changeIpFilter");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, ipfilter: '1' } })
    },

    bandwidthUsage() {
      if (!this.store.state || !this.store.state.sessionStatus || !this.store.state.sessionStatus.accessUsage)
        return null;
      let accessUsage = this.store.state.sessionStatus.accessUsage;
      if (accessUsage.maxTraffic == 0)
        return null;

      let mb = 1000000;
      let gb = 1000 * mb;

      let ret = { used: accessUsage.sentTraffic + accessUsage.receivedTraffic, total: accessUsage.maxTraffic };
      // let ret = { used: 100 * mb, total: 2000 * mb };

      if (ret.total > 1000 * mb) {
        ret.used = (ret.used / gb).toFixed(0) + "GB";
        ret.total = (ret.total / gb) + "GB";
      }
      else {
        ret.used = (ret.used / mb).toFixed(0) + "MB";
        ret.total = (ret.total / mb).toFixed(0) + "MB";
      }

      return ret;
    },

    formatSpeed(speed) {
      return (speed * 10 / 1000000).toFixed(2);
    },
  }
}
</script>
