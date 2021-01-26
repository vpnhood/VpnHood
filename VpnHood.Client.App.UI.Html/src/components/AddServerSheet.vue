<template>
  <v-bottom-sheet v-model="sheetVisible" value="true">
    <v-sheet>
      <!-- Add Test Server -->
      <v-card
        v-if="testServerVisible"
        class="mx-auto ma-5"
        max-width="600"
        flat
      >
        <v-card-title>{{ $t("addTestServer") }}</v-card-title>
        <v-card-subtitle>{{ $t("addTestServerSubtitle") }}</v-card-subtitle>
        <v-btn text @click="addTestServer()">{{ $t("add") }}</v-btn>
        <v-divider class="mt-5" />
      </v-card>

      <!-- Add Private Server -->
      <v-card class="mx-auto" max-width="600" flat>
        <v-card-title>{{ $t("addAcessKeyTitle") }}</v-card-title>
        <v-card-subtitle>{{ $t("addAcessKeySubtitle") }}</v-card-subtitle>
        <v-text-field
          v-model="accessKeyValue"
          spellcheck="false"
          autocomplete="off"
          :error-messages="accessKeyErrorMessage"
          class="mx-5"
          @input="onKeyAccessChanged"
          append-icon="vpn_key"
          :placeholder="accessKeyPrefix"
          solo
        ></v-text-field>
      </v-card>
    </v-sheet>
  </v-bottom-sheet>
</template>

<script>

import { Base64 } from 'js-base64';

export default {
  components: {
  },
  props: {
  },
  created() {
    this.isRouterBusy = false;
  },
  data: () => ({
    accessKeyValue: null,
    accessKeyErrorMessage: null,
    accessKeyPrefix: "vh://"
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
        return this.$route.query.addserver != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          this.$router.back();
        }
      }
    },

    testServerVisible() {
      return this.store.features.testServerTokenId &&
        !this.store.clientProfileItems.find(x => x.clientProfile.tokenId == this.store.features.testServerTokenId);
    }
  },

  methods: {
    async onKeyAccessChanged(value) {
      this.accessKeyErrorMessage = null;

      if (value == null || value == "")
        return;

      if (!this.validateAccessKey(value)) {
        this.accessKeyErrorMessage = this.$t("invalidAccessKeyFormat", { prefix: this.accessKeyPrefix });
        return;
      }

      try {
        await this.store.invoke("addAccessKey", { accessKey: value })
        this.store.loadApp();
        this.$router.back();
      }
      catch (ex) {
        this.accessKeyErrorMessage = ex.toString();
      }
    },

    validateAccessKey(accessKey) {
      try {
        const json = Base64.decode(accessKey);
        return JSON.parse(json) != null;
      }
      catch (ex) {
        return false;
      }
    },

    async addTestServer() {
      await this.store.invoke("addTestServer");
      await this.store.loadApp();
      this.$router.back();
    },
  }
}
</script>
