<template>
  <v-container class="mx-auto ma-0 pa-0" fluid>
    <div class="container-fluid">
      <div id="sectionWrapper" class="row px-5">
        <div id="topSection" class="col-12 pt-4 align-self-start">
          <div class="row">
            <div id="slideMenuBtn" class="col-3 pl-0 pl-md-3">
              <v-app-bar-nav-icon
                color="white"
                @click.stop="store.navigationDrawer = !store.navigationDrawer"
              ></v-app-bar-nav-icon>
            </div>
            <div id="logo" class="col-6 text-center">
              <img src="../assets/img/logo-small.png" alt="VpnHood" />
              <h1 class="h3 mt-1 mb-0">VpnHood</h1>
            </div>
            <div id="settingBtn" class="col-3 pr-0 pr-md-3 text-right">
              <i class="material-icons text-white btn">settings</i>
            </div>
          </div>
        </div>
        <div
          id="middleSection"
          class="col-12 text-center align-self-center disconnected"
        >
          <div id="circleWrapper">
            <div id="circle">
              <div id="circleContent">
                <span class="material-icons connect-icon">power</span>
                <h2 class="h1">{{ this.statusText }}</h2>
                <div id="usageBandwidth">
                  <span>0.3</span>
                  <span class="bandwidthUnit">GB of</span>
                </div>
                <div id="totalBandwidth">
                  <span>10</span>
                  <span class="bandwidthUnit">GB</span>
                </div>
                <span class="material-icons disconnect-icon">power_off</span>
              </div>
            </div>
          </div>

          <v-btn
            id="connect-button"
            class="connect-button"
            v-if="
              store.clientProfile.defaultProfile() &&
              store.state.connectionState == 'None'
            "
            @click="store.connect('$')"
          >
            {{ $t("connect") }}
          </v-btn>
          <v-btn
            v-else-if="store.defaultClientProfileItem"
            class="connect-button"
            @click="disconnect()"
            :disabled="store.state.connectionState == 'Disconnecting'"
          >
            {{ $t("disconnect") }}
          </v-btn>

          <div id="premiumLink" class="mt-3">
            <i class="material-icons">verified</i
            ><span>Go Premium For Unlimited Usage</span>
          </div>
        </div>

        <v-container class="server-info-section align-self-end fluid col-12">
          <v-row align="center">
            <v-col cols="6" class="server-info">
              <span class="sky-blue-text d-block mr-md-2 d-md-inline-block">{{
                $t("selectServer")
              }}</span>
              <span class="pr-2 mr-1 server-name">Public</span>
              <span>192.168.0.1</span>
            </v-col>
            <v-col cols="6" class="text-right pr-0">
              <v-btn to="/servers" text color="white">
                {{ $t("manageServers") }}
                <v-icon flat>keyboard_arrow_right</v-icon>
              </v-btn>
            </v-col>
          </v-row>
        </v-container>
      </div>
    </div>
  </v-container>
</template>

<style scoped>
@import "../assets/styles/custom.css";
</style>

<script>

export default {
  name: 'ServersPage',
  components: {
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
    items() { return this.store.clientProfileItems; },
    currentClientProfile() {
      return this.store.defaultClientProfile;
    },

    statusText() {
      if (!this.currentClientProfile)
        return this.$t("noServerSelected");

      switch (this.currentClientProfile) {
        case "Connecting": return this.$t('connecting');
        case "Connected": return this.$t('connected');
        case "Disconnecting": return this.$t('disconnecting');
        case "Diagnosing": return this.$t('diagnosing');
        default: return this.$t('disconnected');
      }
    },
  },

  methods: {
    clientProfileItem_state(item) {
      return (this.store.state.activeClientProfileId == item.clientProfile.clientProfileId)
        ? this.store.state.connectionState : "None";
    },

    connect() {
      window.gtag('event', 'connect');
      this.store.state.hasDiagnosedStarted = false;
      this.store.state.activeClientProfileId = this.store.state.defaultClientProfileId;
      this.store.invoke("connect", { clientProfileId: this.store.state.defaultClientProfileId });
    },

    diagnose(item) {
      window.gtag('event', 'diagnose');
      this.store.state.hasDiagnosedStarted = true;
      this.store.state.activeClientProfileId = item.clientProfile.clientProfileId;
      this.store.invoke("diagnose", { clientProfileId: item.clientProfile.clientProfileId });
    },

    disconnect() {
      window.gtag('event', 'disconnect');
      this.store.state.connectionState = "Disconnecting";
      this.store.invoke("disconnect");
    },

    getProfileItemName(item) {
      if (item.clientProfile.name && item.clientProfile.name.trim() != '') return item.clientProfile.name;
      else if (item.token.name && item.token.name.trim() != '') return item.token.name;
      else return this.$t('noname');

    },
  }
}
</script>
