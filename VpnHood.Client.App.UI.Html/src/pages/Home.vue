<template>
  <v-container id="sectionWrapper" fill-height fluid class="px-4 pt-4 px-sm-8 pt-sm-5">
    <v-row class="align-self-start">
      <!-- top bar -->
      <v-col cols="3" class="pa-0 ma-0">
        <v-app-bar-nav-icon
          color="white"
          @click.stop="store.navigationDrawer = !store.navigationDrawer"
        ></v-app-bar-nav-icon>
      </v-col>
      <v-col cols="6" id="logo" class="text-center pb-0">
        <img src="@/assets/images/logo-small.png" :alt="$t('appName')" />
        <h1 class="">{{ $t("appName") }}</h1>
      </v-col>
      <v-col cols="3" class="text-right pa-0 ma-0">
        <!-- Menu -->
        <ClientProfileMenu
          clientProfileId="$"
          color="white"
          :showAddServerItem="true"
          :showDeleteItem="false"
          :showRenameItem="false"
        />
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

              <div id="bandwidthUsage" v-if="connectionState == 'Connected' && this.bandwidthUsage()>1">
                <span>{{this.bandwidthUsage().used}} of</span>
              </div>
              <div id="bandwithTotal" v-if="connectionState == 'Connected'">
                <span>{{ 10000000000 }}</span>
              </div>
              <v-icon
                class="state-icon"
                v-if="connectionState == 'None'"
                size="90"
                color="white"
                >power_off</v-icon
              >
              <v-icon
                class="state-icon"
                v-else-if="connectionState == 'Connecting'"
                size="90"
                color="white"
                >power</v-icon
              >
              <v-icon
                class="state-icon"
                v-else-if="connectionState == 'Diagnosing'"
                size="90"
                color="white"
                >network_check</v-icon
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

    <!-- ServerInfo -->
    <v-row id="serverInfoSection" class="align-self-end">
      <v-col id="serverInfo" cols="6" sm="auto" class="pr-0 mr-0 pb-0">
        <span class="sky-blue-text mr-0 pr-0">{{
          $t("selectedServer")
        }}</span>
      </v-col>
      <!-- serverChange -->
      <v-col cols="6" md="auto" class="text-right pb-0" order="sm-3">
        <v-btn text color="white" @click="showServersSheet" small>
          {{ $t("manageServers") }}
          <v-icon flat>keyboard_arrow_right</v-icon>
        </v-btn>
      </v-col>
      <!-- serverName -->
      <v-col cols="12" sm="" order="sm-2" class="pt-0 pt-sm-3">
        <span id="serverName" class="pr-2 mr-1 ">{{
          store.clientProfile.name("$")
        }}</span>
      </v-col>
    </v-row>
  </v-container>
  <!-- rootContaier -->
</template>

<style>
@import "../assets/styles/custom.css";
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
    connectionState() { return this.store.connectionState("$"); }
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

    bandwidthUsage()
    {
      console.log(this.store.state);
      if (!this.store.state || !this.store.state.sessionStatus || !this.store.state.sessionStatus.accessUsage)
        return null;
      let accessUsage = this.store.state.sessionStatus.accessUsage;
      let used = accessUsage.sentByteCount + accessUsage.receivedByteCount;
      let total = accessUsage.MaxTrafficByteCount;
      return {used, total};
    },

  }
}
</script>
