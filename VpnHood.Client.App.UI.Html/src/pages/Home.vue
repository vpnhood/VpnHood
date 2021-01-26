<template>
  <div class="container-fluid">
    <div id="sectionWrapper" class="row px-5">
      <div class="col-12 pt-4 align-self-start">
        <div class="row">
          <!-- top bar -->
          <div class="col-3 pl-0 pl-md-3">
            <v-app-bar-nav-icon
              color="white"
              @click.stop="store.navigationDrawer = !store.navigationDrawer"
            ></v-app-bar-nav-icon>
          </div>
          <div id="logo" class="col-6 text-center">
            <img src="../assets/images/logo-small.png" alt="VpnHood" />
            <h1 class="h3 mt-1 mb-0">VpnHood</h1>
          </div>
          <div class="col-3 pr-0 pr-md-3 text-right">
            <!-- Item Menu -->
            <v-menu left transition="slide-y-transition">
              <template v-slot:activator="{ on, attrs }">
                <v-btn v-bind="attrs" v-on="on" icon>
                  <v-icon color="white">mdi-dots-vertical</v-icon>
                </v-btn>
              </template>
              <v-list>
                <!-- Rename -->
                <v-list-item link @click="editClientProfile('$')">
                  <v-list-item-icon>
                    <v-icon>edit</v-icon>
                  </v-list-item-icon>
                  <v-list-item-title>{{ $t("rename") }}</v-list-item-title>
                </v-list-item>

                <!-- Diagnose -->
                <v-list-item
                  link
                  @click="store.diagnose('$')"
                  :disabled="store.state.hasDiagnoseStarted"
                >
                  <v-list-item-icon>
                    <v-icon>network_check</v-icon>
                  </v-list-item-icon>
                  <v-list-item-title>{{ $t("diagnose") }}</v-list-item-title>
                </v-list-item>
              </v-list>
            </v-menu>
          </div>
        </div>
      </div>

      <v-container
        id="middleSection"
        :class="`state-${connectionState.toLowerCase()}`"
      >
        <!-- Circles -->
        <v-row align="center">
          <v-col cols="12">
            <div id="circleOuter">
              <div id="circle">
                <div id="circleContent" class="align-center">
                  <span id="stateText">{{
                    store.connectionStateText("$")
                  }}</span>

                  <div
                    id="bandwidthUsage"
                    v-if="connectionState == 'Connected'"
                  >
                    <span>0.3</span>
                    <span>GB of</span>
                  </div>
                  <div id="bandwithTotal" v-if="connectionState == 'Connected'">
                    <span>10</span>
                    <span>GB</span>
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
          </v-col>
        </v-row>

        <!-- main button -->
        <v-row align="center" class="pt-5">
          <v-col cols="12">
            <v-layout justify-center align-center align-content-center>
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
            </v-layout>
          </v-col>
        </v-row>
      </v-container>

      <v-container id="serverInfoSection" class="align-self-end mb-2">
        <v-row align="center">
          <v-col cols="6" id="serverInfo">
            <span class="sky-blue-text d-block mr-md-2 d-md-inline-block">{{
              $t("selectedServer")
            }}</span>
            <span id="serverName" class="pr-2 mr-1">Public</span>
            <span>192.168.0.1</span>
          </v-col>
          <v-col cols="6" class="text-right pa-0">
            <v-btn text color="white" @click="showServersSheet">
              {{ $t("manageServers") }}
              <v-icon flat>keyboard_arrow_right</v-icon>
            </v-btn>
          </v-col>
        </v-row>
      </v-container>
    </div>
  </div>
</template>

<style>
@import "../assets/styles/custom.css";
</style>

<script>

export default {
  name: "HomePage",
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

  }
}
</script>
