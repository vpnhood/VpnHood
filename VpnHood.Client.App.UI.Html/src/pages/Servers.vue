<template>
  <v-card max-width="600" class="mx-auto" flat>
    <!-- Server lists -->
    <v-container>
      <v-row dense>
        <v-col v-for="(item, i) in store.clientProfileItems" :key="i" cols="12">
          <v-expand-transition>
            <v-card
              :loading="
                store.clientProfile.connectionState(
                  item.clientProfile.clientProfileId
                ) != 'None' &&
                store.clientProfile.connectionState(
                  item.clientProfile.clientProfileId
                ) != 'Connected'
                  ? store.state.hasDiagnoseStarted
                    ? 'warning'
                    : true
                  : false
              "
              :color="
                store.clientProfile.connectionState(
                  item.clientProfile.clientProfileId
                ) == 'Connected'
                  ? 'green accent-1'
                  : ''
              "
              style="transition: background-color 1s"
            >
              <div class="d-flex flex-no-wrap justify-space-between">
                <div>
                  <v-card-title v-text="store.clientProfile.name(item.clientProfile.clientProfileId)" />
                  <v-card-subtitle v-text="item.token.ep" />
                  <v-card-text>
                    {{ store.clientProfile.statusText(item) }}
                  </v-card-text>
                  <v-card-actions>
                    <v-btn
                      v-if="
                        store.state.activeClientProfileId !=
                        item.clientProfile.clientProfileId
                      "
                      class="ma-2"
                      @click="store.connect(item.clientProfile.clientProfileId)"
                    >
                      {{ $t("connect") }}
                    </v-btn>
                    <v-btn
                      v-else
                      class="ma-2"
                      @click="store.disconnect()"
                      :disabled="
                        store.clientProfile.connectionState(
                          item.clientProfile.clientProfileId
                        ) == 'None'
                      "
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
                      @click="
                        editClientProfile(item.clientProfile.clientProfileId)
                      "
                    >
                      <v-list-item-title>{{ $t("rename") }}</v-list-item-title>
                    </v-list-item>

                    <!-- Diagnose -->
                    <v-list-item
                      link
                      @click="
                        store.diagnose(item.clientProfile.clientProfileId)
                      "
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
            <v-btn @click="showAddServerSheet()" class="ma-16" text>
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
  },
  methods: {
    async remove(item) {
      const clientProfileId = item.clientProfile.clientProfileId;
      const res = await this.$confirm(this.$t("confirmRemoveServer"), { title: this.$t("warning") })
      if (res) {
        await this.store.invoke("removeClientProfile", { clientProfileId });
        this.store.loadApp();
      }
    },

    showAddServerSheet() {
      window.gtag('event', "addServerButton");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, addserver: '1' } })
    },

    editClientProfile(clientProfileId) {
      window.gtag('event', "editprofile");
      this.$router.push({ path: this.$route.path, query: { ... this.$route.query, editprofile: clientProfileId } })
    },
  }
}
</script>
