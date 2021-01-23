<template>
  <v-card max-width="600" class="mx-auto" flat>
    
    <!-- Server lists -->
    <v-container>
      <v-row dense>
        <v-col v-for="(item, i) in items" :key="i" cols="12">
          <v-expand-transition>
            <v-card
              v-if="
                !store.state.activeClientProfileId ||
                store.state.activeClientProfileId ==
                  item.clientProfile.clientProfileId
              "
              :loading="
                clientProfileItem_state(item) != 'None' &&
                clientProfileItem_state(item) != 'Connected'
                  ? store.state.hasDiagnoseStarted
                    ? 'warning'
                    : true
                  : false
              "
              :color="
                clientProfileItem_state(item) == 'Connected'
                  ? $vuetify.theme.dark
                    ? 'green accent-4'
                    : 'green accent-1'
                  : ''
              "
              style="transition: background-color 1s"
            >
              <div class="d-flex flex-no-wrap justify-space-between">
                <div>
                  <v-card-title v-text="getProfileItemName(item)" />
                  <v-card-subtitle v-text="item.token.ep" />
                  <v-card-text>
                    {{ clientProfileItem_statusText(item) }}
                  </v-card-text>
                  <v-card-actions>
                    <v-btn
                      v-if="
                        store.state.activeClientProfileId !=
                        item.clientProfile.clientProfileId
                      "
                      class="ma-2"
                      @click="connect(item)"
                    >
                      {{ $t("connect") }}
                    </v-btn>
                    <v-btn
                      v-else
                      class="ma-2"
                      @click="disconnect()"
                      :disabled="clientProfileItem_state(item) == 'None'"
                    >
                      {{ $t("disconnect") }}
                    </v-btn>
                  </v-card-actions>
                </div>

                <!-- Item Menu -->
                <v-menu left transition="slide-y-transition">
                  <template v-slot:activator="{ on, attrs }">
                    <v-btn v-bind="attrs" v-on="on" icon>
                      <v-icon>mdi-dots-vertical</v-icon>
                    </v-btn>
                  </template>
                  <v-list>
                    <!-- Rename -->
                    <v-list-item
                      link
                      :to="'?editprofile=' + item.clientProfile.clientProfileId"
                    >
                      <v-list-item-title>{{ $t("rename") }}</v-list-item-title>
                    </v-list-item>

                    <!-- Diagnose -->
                    <v-list-item
                      link
                      @click="diagnose(item)"
                      :disabled="store.state.hasDiagnoseStarted"
                    >
                      <v-list-item-title>{{
                        $t("diagnose")
                      }}</v-list-item-title>
                    </v-list-item>

                    <!-- Delete -->
                    <v-divider />
                    <v-list-item link @click="remove(item)">
                      <v-list-item-title>{{ $t("remove") }}</v-list-item-title>
                    </v-list-item>
                  </v-list>
                </v-menu>
              </div>
            </v-card>
          </v-expand-transition>
        </v-col>

        <!-- Add Server Button -->
        <v-col cols="12" v-if="!store.state.activeClientProfileId">
          <v-card class="text-center">
            <v-btn
              @click="showAddServerSheet()"
              class="ma-16"
              text
            >
              {{ $t("addServer") }}
            </v-btn>
          </v-card>
        </v-col>
      </v-row>
    </v-container>
  </v-card>
</template>

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
  },
  methods: {
    clientProfileItem_state(item) {
      return (this.store.state.activeClientProfileId == item.clientProfile.clientProfileId)
        ? this.store.state.connectionState : "None";
    },

    clientProfileItem_statusText(item) {
      switch (this.clientProfileItem_state(item)) {
        case "Connecting": return this.$t('connecting');
        case "Connected": return this.$t('connected');
        case "Disconnecting": return this.$t('disconnecting');
        case "Diagnosing": return this.$t('diagnosing');
        default: return this.$t('disconnected');
      }
    },

    connect(item) {
      window.gtag('event', 'connect');
      this.store.state.hasDiagnosedStarted = false;
      this.store.state.activeClientProfileId = item.clientProfile.clientProfileId;
      this.store.invoke("connect", { clientProfileId: item.clientProfile.clientProfileId });
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

    async remove(item) {
      const clientProfileId = item.clientProfile.clientProfileId;
      const res = await this.$confirm(this.$t("confirmRemoveServer"), { title: this.$t("warning") })
      if (res) {
        await this.store.invoke("removeClientProfile", { clientProfileId });
        this.store.loadApp();
      }
    },
    getProfileItemName(item) {
      if (item.clientProfile.name && item.clientProfile.name.trim() != '') return item.clientProfile.name;
      else if (item.token.name && item.token.name.trim() != '') return item.token.name;
      else return this.$t('noname');

    },
    showAddServerSheet() {
      window.gtag('event', "addServerButton");
      this.$router.push({path: this.$route.path, query: { ... this.$route.query, addserver: '1' }})
    },
  }
}
</script>
