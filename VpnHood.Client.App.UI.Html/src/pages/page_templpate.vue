<template>
  <v-dialog transition="dialog-bottom-transition" v-model="dialog">
    <v-card>
    </v-card>
  </v-dialog>
</template>

<script>

export default {
  name: 'Feedback',
  components: {
  },
  created() {
    this.store.setTitle(this.$t("page_templpate"));
    this.formLoad = 0;
    this.updateToolbars();
  },
  mounted() {
  },

  data: () => ({
    dialog: true
  }),
  computed: {
  },
  methods: {
    updateToolbars() {
      this.store.toolbarItems = [
      ]
    },
  }
}
</script>
