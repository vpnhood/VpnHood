<template>
  <v-dialog
    v-model="sheetVisible"
    value="true"
    @click:outside="cancel()"
    :transition="isMobileSize ? 'dialog-bottom-transition' : ''"
    :max-width="isMobileSize ? '' : 600"
  >
    <v-card v-if="sheetVisible">
      <v-card-title>
        {{ $t("rename") }}
      </v-card-title>
      <v-card-text>
        <v-text-field
          v-model="clientProfile.name"
          label=""
          spellcheck="false"
          autocomplete="off"
          required
        ></v-text-field>
      </v-card-text>
      <v-card-actions>
        <v-spacer></v-spacer>
        <v-btn color="blue darken-1" text @click="cancel()">
          {{ $t("cancel") }}
        </v-btn>
        <v-btn color="blue darken-1" text @click="save()">
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
    sheetVisible: {
      get() {
        return this.$route.query.editprofile != null;
      },
      set(value) {
        if (!value && !this.isRouterBusy) {
          this.isRouterBusy = true;
          ///this.$router.back(); oncancel is already handled
        }
      }
    },

    clientProfile() {
      let clientProfileItem = this.store.clientProfileItems.find(x => x.clientProfile.clientProfileId == this.$route.query.editprofile);
      return clientProfileItem ? clientProfileItem.clientProfile : null;
    },

    isMobileSize() { return this.$vuetify.breakpoint.smAndDown; },
  },

  methods: {

    async cancel() {
      this.store.loadApp();
      this.$router.back();
    },

    async save() {
      const clientProfile = this.clientProfile;
      await this.store.invoke("setClientProfile", { clientProfile });
      this.store.loadApp();
      this.$router.back();
    },

  }
}
</script>
