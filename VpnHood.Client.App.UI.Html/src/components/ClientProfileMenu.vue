<template>
  <v-menu left transition="slide-y-transition">
    <template v-slot:activator="{ on, attrs }">
      <v-btn v-bind="attrs" v-on="on" icon :color="color">
        <v-icon>mdi-dots-vertical</v-icon>
      </v-btn>
    </template>
    <v-list>
      <!-- Add Server -->
      <v-list-item link @click="showAddServerSheet()" v-if="showAddServerItem">
        <v-list-item-icon>
          <v-icon>add</v-icon>
        </v-list-item-icon>
        <v-list-item-title>{{ $t("addServer") }}</v-list-item-title>
      </v-list-item>

      <!-- Manage Servers -->
      <v-list-item
        link
        @click="showServersSheet()"
        v-if="showManageServerItem"
      >
        <v-list-item-icon>
          <v-icon>dns</v-icon>
        </v-list-item-icon>
        <v-list-item-title>{{ $t("manageServers") }}</v-list-item-title>
      </v-list-item>
      <v-divider v-if="showAddServerItem || showManageServerItem" />

      <!-- Rename -->
      <v-list-item
        v-if="showRenameItem"
        link
        @click="editClientProfile(clientProfileId)"
      >
        <v-list-item-icon>
          <v-icon>edit</v-icon>
        </v-list-item-icon>
        <v-list-item-title>{{ $t("rename") }}</v-list-item-title>
      </v-list-item>

      <!-- Diagnose -->
      <v-list-item
        link
        @click="store.diagnose(clientProfileId)"
        :disabled="store.state.hasDiagnoseStarted"
      >
        <v-list-item-icon>
          <v-icon>network_check</v-icon>
        </v-list-item-icon>
        <v-list-item-title>{{ $t("diagnose") }}</v-list-item-title>
      </v-list-item>

      <!-- Delete -->
      <v-divider v-if="showDeleteItem" />
      <v-list-item v-if="showDeleteItem" link @click="remove(clientProfileId)">
        <v-list-item-icon>
          <v-icon>delete</v-icon>
        </v-list-item-icon>
        <v-list-item-title>{{ $t("remove") }}</v-list-item-title>
      </v-list-item>
    </v-list>
  </v-menu>
</template>

<script>

export default {
  components: {
  },
  props: {
    clientProfileId: String,
    showAddServerItem: { type: Boolean, default: true },
    showManageServerItem: { type: Boolean, default: true },
    showDeleteItem: { type: Boolean, default: true },
    showRenameItem: { type: Boolean, default: true },
    color: { type: String, default: "" }
  },
  created() {
  },
  data: () => ({
  }),
  watch:
  {
  },
  computed: {
  },
  methods: {
    async remove(clientProfileId) {
      clientProfileId = this.store.clientProfile.updateId(clientProfileId);
      const res = await this.$confirm(this.$t("confirmRemoveServer", { serverName: this.store.clientProfile.name(clientProfileId) }), { title: this.$t("warning") })
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
