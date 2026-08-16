import { Strings } from './strings';

/**
 * The English Dashboard (D79).
 *
 * German is primary; this is the second language, so a non-German-speaking user can operate the
 * whole workflow. It is **annotated** with `Strings`, not merely checked against it, so a key added
 * to `de.ts` without a translation here is a compile error.
 *
 * Two wording rules:
 *
 * - **The commercial documents are translated: *Angebot* → "Quote", *Rechnung* → "Invoice".** An
 *   English-speaking user has to be able to operate the commercial workflow, and a UI that keeps
 *   saying *Angebot* does not let them.
 * - **The domain keeps its own names.** `Angebot` remains the aggregate, the route segment and the
 *   API resource. Only the label a user reads changes.
 */
export const EN: Strings = {
  app: {
    name: 'RenoTrack',
    dashboard: 'RenoTrack Dashboard',
  },

  nav: {
    cockpit: 'Cockpit',
    leads: 'Leads',
    inspections: 'Site visits',
    angebote: 'Quotes',
    invoices: 'Invoices',
    projects: 'Projects',
    catalog: 'Catalog',
    notifications: 'Notifications',
    logout: 'Sign out',
    mainNavigation: 'Main navigation',
    skipToContent: 'Skip to content',
    openMenu: 'Open menu',
  },

  roles: {
    admin: 'Office',
    inspector: 'Site management',
  },

  actions: {
    save: 'Save',
    cancel: 'Cancel',
    close: 'Close',
    retry: 'Try again',
    reset: 'Reset',
    refresh: 'Refresh',
    open: 'Open',
    login: 'Sign in',
    showAll: 'Show all',
    back: 'Back',
    create: 'Create',
    confirm: 'Confirm',
    remove: 'Remove',
    /** Heading for the actions column — screen readers only; it stays visually empty. */
    actionsColumn: 'Actions',
  },

  states: {
    loading: 'Loading …',
    emptyTitle: 'Nothing here',
    emptyBody: 'There is nothing to show for the current selection.',
    errorTitle: 'Could not load data',
    errorBody: 'Please try again. If the problem continues, contact your administrator.',
    noResultsTitle: 'No matches',
    noResultsBody: 'Adjust the filters to see more results.',
  },

  errors: {
    validation: 'The input is incomplete or invalid.',
    unauthenticated: 'Your session has expired. Please sign in again.',
    forbidden: 'You do not have permission for this action.',
    notFound: 'The record could not be found.',
    conflict: 'This record changed in the meantime. Please reload the page and try again.',
    gone: 'This link has expired.',
    offline: 'No connection to the server. Please check your network.',
    server: 'The server ran into an error.',
  },

  login: {
    title: 'Sign in',
    subtitle: 'Internal area for office administration and site management.',
    email: 'Email',
    password: 'Password',
    submit: 'Sign in',
    signingIn: 'Signing in …',
    failed:
      'Sign-in failed. Please check the email address and password. After several failed attempts the account is temporarily locked.',
    emailRequired: 'Please enter a valid email address',
    passwordRequired: 'Please enter your password',
  },

  cockpit: {
    greetingMorning: 'Good morning',
    greetingDay: 'Good afternoon',
    greetingEvening: 'Good evening',
    subtitleAdmin: 'How the business stands, at a glance.',
    subtitleInspector: 'Your sites, appointments and quotes.',
    updatedAt: 'As of',

    kpi: {
      revenue: 'Paid (year)',
      revenueHint: 'Payments actually received.',
      openQuotes: 'Open quote volume',
      openQuotesHint: 'With customers, awaiting a decision.',
      outstanding: 'Outstanding',
      outstandingHint: 'Invoiced and not yet paid.',
      overdue: 'Overdue',
      overdueHint: 'Past the payment date.',
      projects: 'Active projects',
      projectsHint: 'Sites currently in progress.',
      decisions: 'Decisions',
      decisionsHint: 'Items waiting on you.',

      today: 'Appointments today',
      todayHint: 'Your on-site visits.',
      quotesToWrite: 'Quotes to write',
      quotesToWriteHint: 'Visit done, quote outstanding.',
      myQuotes: 'My quotes',
      myQuotesHint: 'Being written or in review.',
      week: 'Appointments this week',
      weekHint: 'Scheduled site visits.',
    },

    money: {
      heading: 'Invoices',
      subheading: 'Everything invoiced so far this year.',
      total: 'Invoiced',
      paid: 'Paid',
      open: 'Open',
      overdue: 'Overdue',
      voided: 'Voided',
      empty: 'No invoices raised yet.',
    },

    funnel: {
      heading: 'From enquiry to order',
      subheading: 'Where work stalls — and how much money is sitting there.',
      empty: 'No enquiries recorded yet.',
      stages: {
        New: 'New enquiries',
        InspectionScheduled: 'Visit scheduled',
        InspectionDone: 'Visit done',
        AngebotInProgress: 'Quote in progress',
        AngebotSent: 'With customer',
        Won: 'Won',
      },
      conversion: 'Win rate {0} % — {1} of {2} decided enquiries won.',
    },

    decisions: {
      heading: 'What needs you now',
      subheading: 'By urgency. Every entry opens the work itself.',
      nothing: 'Nothing urgent',
      nothingBody: 'No item is waiting on your decision right now.',
      quotesToApprove: 'Quotes to approve',
      quotesToApproveHint: 'Waiting on the office',
      invoicesOverdue: 'Invoices overdue',
      invoicesOverdueHint: 'Past the payment date',
      awaitingCustomer: 'Customers not responding',
      awaitingCustomerHint: 'Quote sent, no decision yet',
      leadsToSchedule: 'Schedule a site visit',
      leadsToScheduleHint: 'New enquiries with no appointment',
      quotesToWrite: 'Quotes to write',
      quotesToWriteHint: 'Visit done, quote outstanding',
      inspectionsToday: 'Site visits today',
      inspectionsTodayHint: "On today's schedule",
    },

    schedule: {
      heading: 'Schedule',
      subheading: 'On site today and tomorrow.',
      today: 'Today',
      tomorrow: 'Tomorrow',
      empty: 'No appointments',
      done: 'done',
    },

    projects: {
      heading: 'Projects under way',
      subheading: 'Order value and status of the active sites.',
      empty: 'No active projects.',
    },
  },

  leads: {
    title: 'Leads',
    subtitleAdmin: 'Every enquiry at a glance.',
    subtitleInspector: 'Your assigned enquiries.',
    count: 'enquiries',
    readOnlyHint: 'Status is not changed here — it moves through the action that causes it.',
    columns: {
      customer: 'Customer',
      contact: 'Contact',
      source: 'Source',
      status: 'Status',
      inspector: 'Site management',
      created: 'Received',
    },
    unassigned: 'Unassigned',
    newLead: 'New lead',
  },

  /** The form for manual entry (FR-2.1) and for correcting contact details. */
  leadForm: {
    createTitle: 'Add a lead',
    createHint:
      'For enquiries that arrive by telephone or email. Enquiries from the website contact form are created automatically.',
    editTitle: 'Correct contact details',
    editHint:
      'Only the contact details change. Status, assignment and notes are left untouched.',
    name: 'Name',
    phone: 'Telephone',
    email: 'Email',
    address: 'Address',
    notes: 'Notes',
    source: 'Source',
    optional: 'optional',
    created: 'Lead created.',
    updated: 'Contact details updated.',
  },

  angebote: {
    title: 'Quotes',
    subtitle: 'Every quote, its status and its value.',
    count: 'quotes',
    columns: {
      number: 'Number',
      customer: 'Customer',
      status: 'Status',
      net: 'Net',
      gross: 'Gross',
      created: 'Created',
      sent: 'Sent',
    },
    totalValue: 'Total (gross)',
    status: {
      Draft: 'Draft',
      InReview: 'In review',
      ChangesRequested: 'Changes requested',
      ApprovedInternally: 'Approved internally',
      Sent: 'Sent',
      CustomerApproved: 'Accepted',
      CustomerRejected: 'Declined',
    },
  },

  /** The quote document — Wireframes D1 (builder) and D3 (review). */
  angebotDetail: {
    backToList: 'Back to quotes',
    documentLabel: 'Quote',
    createdOn: 'created on',
    grossTotal: 'Total (gross)',
    netTotal: 'Net amount',
    summary: 'Summary',
    vatLine: 'plus {0} % VAT',
    positions: 'Positions',
    positionsHint: 'The sections and line items of this quote.',
    position: 'Pos.',
    subtotal: 'Subtotal',
    emptyTitle: 'No positions yet',
    emptyBody: 'Add a section first, then add line items to it.',
    emptyBodyRead: 'No positions have been recorded for this quote yet.',
    sectionEmpty: 'This section has no line items yet.',

    addSection: 'Add section',
    addSectionHint: 'Sections structure the quote, e.g. "Site setup".',
    sectionTitle: 'Title',
    removeSection: 'Remove section',
    addItem: 'Add line item',
    addItemHint: 'Take it from the Catalog, or enter it freely.',
    specification: 'Specification',
    fromCatalog: 'from Catalog',
    fromCatalogAction: 'Choose from Catalog',
    linkedToCatalog: 'Linked to a Catalog entry',
    makeCustom: 'Unlink',
    customItemHint: 'Custom line — description and unit are entered here.',
    customUnit: 'Custom unit',
    standardUnit: 'Standard unit',
    saveToCatalog: 'Save to Catalog',
    required: 'Required',

    columns: {
      description: 'Item',
      quantity: 'Qty',
      unit: 'Unit',
      unitPrice: 'Unit price',
      vat: 'VAT',
      lineTotal: 'Total',
      actions: 'Actions',
    },

    submitForReview: 'Submit for review',
    requestChanges: 'Request changes',
    requestChangesHint:
      'Site management receives your comment and can edit the quote again afterwards.',
    comment: 'Comment',
    approve: 'Approve',
    send: 'Send to customer',
    convert: 'Convert to project',

    reviewHistory: 'Review history',
    reviewHistoryHint: 'Every response the office has given on this quote.',
    noComments: 'No responses yet.',
    byAdmin: 'Office',

    awaitingRework:
      'The office has requested changes. As soon as you edit the quote it becomes a draft again and can be resubmitted.',
    inReviewHint: 'This quote is with the office for review.',

    sectionAdded: 'Section added.',
    sectionRemoved: 'Section removed.',
    itemAdded: 'Line item added.',
    itemRemoved: 'Line item removed.',
    savedToCatalog: 'The item was saved to the Catalog.',
    submitted: 'Quote submitted for review.',
    approved: 'Quote approved.',
    sent: 'The quote was sent to the customer.',
    changesRequested: 'Changes were requested.',
    converted: 'Project created.',

    duplicate: 'Use as a template',
    duplicateTitle: 'Reuse this quote for another enquiry',
    duplicateHint:
      'A new draft is created with the same sections and lines. This quote is left untouched.',
    duplicateTarget: 'Target enquiry',
    duplicateTargetHint:
      'Your own enquiries are listed. If one already has a quote in progress, the copy is refused.',
    noDuplicateTargets: 'You have no other enquiry assigned to you at the moment.',
    duplicated: 'A new draft was created from this quote.',

    confirm: {
      submitTitle: 'Submit this quote?',
      submitBody:
        'The quote goes to the office for review and cannot be edited until they respond.',
      approveTitle: 'Approve this quote?',
      approveBody: 'Once approved, the quote can be sent to the customer.',
      sendTitle: 'Send this quote to the customer?',
      sendBody:
        'The customer receives an email with a personal link to accept or decline. This cannot be undone.',
      convertTitle: 'Create the project?',
      convertBody:
        'The accepted quote becomes a project with the agreed order value.',
    },
  },

  /** The Catalog picker dialog (Wireframe D2). */
  catalog: {
    pickerTitle: 'Search the Catalog',
    pickerHint: 'Title, unit and specification are taken from the Catalog entry itself.',
    search: 'Search term',
    searchPrompt: 'Enter a search term.',
    noResults: 'No matching Catalog entries.',

    // Catalog management (Wireframe F1). Both roles read it; only Admin curates.
    title: 'Catalog',
    subtitle: 'The shared library of services behind every quote.',
    count: 'entries',
    columns: {
      title: 'Title',
      specification: 'Specification',
      unit: 'Unit',
      price: 'Unit price',
      created: 'Added',
    },
    newItem: 'New entry',
    createTitle: 'Add a Catalog entry',
    editTitle: 'Edit Catalog entry',
    itemTitle: 'Title',
    specification: 'Specification',
    unit: 'Unit',
    price: 'Unit price (net)',
    retire: 'Retire',
    retireTitle: 'Retire this Catalog entry?',
    retireBody:
      'The entry disappears from search and from this list. It is not deleted: existing quotes keep their link to it, and it stays valid as a direct reference (BR-12, BR-14).',
    itemCreated: 'Catalog entry created.',
    itemUpdated: 'Catalog entry updated.',
    itemRetired: 'Catalog entry retired.',
    editHint:
      'Changes apply to new quotes only. Lines already written into a quote are unaffected (BR-8).',
    readOnlyHint: 'The Catalog is curated by the office.',
    contributeHint: 'You add your own lines to the Catalog from within a quote.',
  },

  /** The Lead detail page (Wireframe C1) and the two actions that move an enquiry. */
  leadDetail: {
    backToPipeline: 'Back to the pipeline',
    label: 'Enquiry',
    contact: 'Contact',
    phone: 'Phone',
    email: 'Email',
    address: 'Address',
    inspector: 'Site management',
    notes: 'Notes',
    scheduleInspection: 'Schedule site visit',
    scheduleHint: 'The chosen site manager is assigned to this enquiry at the same time (BR-13).',
    dateTime: 'Date and time',
    chooseInspector: 'Please choose',
    createAngebot: 'Create quote',
    angebote: 'Quotes for this enquiry',
    angeboteHint: 'Every quote written for this enquiry.',
    noAngebote: 'No quote has been written for this enquiry yet.',
    scheduled: 'Site visit scheduled.',
    angebotCreated: 'Draft quote created.',

    editContact: 'Edit contact details',
    assignInspector: 'Assign site management',
    changeInspector: 'Change site management',
    assignTitle: 'Assign site management',
    assignHint:
      'The assigned colleague sees this enquiry in their own list. The status of the enquiry is unchanged.',
    assigned: 'Site management assigned.',
  },

  /** The on-site view (Wireframe C3) — the one deliberately mobile-first screen. */
  inspectionDetail: {
    backToSchedule: 'Back to the schedule',
    label: 'Site visit',
    onSite: 'On site',
    openLead: 'Open enquiry',
    photosHint: 'Photos record the condition before any work begins.',
    addPhoto: 'Upload photo',
    markComplete: 'Complete site visit',
    confirmTitle: 'Complete this site visit?',
    confirmBody:
      'No further photos or notes can be added afterwards (BR-10). The enquiry is then ready for a quote.',
    completedHint: 'This site visit is complete and can no longer be changed.',
    notesSaved: 'Notes saved.',
    photoUploaded: 'Photo uploaded.',
    completed: 'Site visit completed.',

    reassign: 'Change site management',
    reassignTitle: 'Hand this visit to a colleague',
    reassignHint:
      'The enquiry is assigned to the same colleague (BR-13). Once the visit is completed it can no longer be handed over.',
    reassigned: 'Site visit handed over.',
  },

  /** The Project detail page and its invoice workflow (Wireframes E1–E3). */
  projectDetail: {
    backToList: 'Back to projects',
    label: 'Project',
    fromAngebot: 'from quote',
    originatingLead: 'Open enquiry',
    agreed: 'Agreed total',
    invoiced: 'Invoiced',
    remaining: 'Left to invoice',
    overInvoiced:
      'More has been invoiced than agreed. That is allowed, but worth checking (BR-3).',

    invoices: 'Invoices',
    invoicesHint: 'Every invoice on this project, voided ones included.',
    noInvoices: 'No invoice has been raised for this project yet.',
    addInvoice: 'Add invoice',
    addInvoiceHint: "The amount is split automatically across the quote's VAT rates.",
    grossAmount: 'Amount (gross)',
    dueDate: 'Due on',

    send: 'Send',
    sendInvoiceTitle: 'Send this invoice to the customer?',
    sendInvoiceBody:
      'The customer receives an email with a personal link to the invoice. This cannot be undone.',
    markPaid: 'Mark as paid',
    markPaidHint: 'The full invoice amount is always recorded.',
    paidAt: 'Paid on',
    method: 'Payment method',
    void: 'Void',
    voidTitle: 'Void this invoice?',
    voidHint: 'The invoice keeps its number and is marked as voided (BR-9).',
    voidReason: 'Reason',

    completeProject: 'Complete project',
    completed: 'Project completed.',
    overrideTitle: 'Complete the project despite open invoices?',
    overrideBody:
      'There are still open invoices, or none at all. Completion is possible with a reason, which is recorded.',
    overrideReason: 'Reason',
    completeAnyway: 'Complete anyway',

    invoiceCreated: 'Invoice created.',
    invoiceSent: 'The invoice was sent to the customer.',
    invoicePaid: 'Payment recorded.',
    invoiceVoided: 'Invoice voided.',

    putOnHold: 'Put on hold',
    holdTitle: 'Put this project on hold?',
    holdBody:
      'Work pauses for now — waiting on materials, say. Invoicing is unaffected and can continue.',
    resume: 'Resume project',
    resumeTitle: 'Resume this project?',
    resumeBody: 'The project goes back to being active.',
    heldMessage: 'Project put on hold.',
    resumedMessage: 'Project resumed.',
    onHoldHint: 'This project is on hold. It has to be resumed before it can be completed.',
  },

  paymentMethod: {
    BankTransfer: 'Bank transfer',
    Cash: 'Cash',
    Other: 'Other',
  },

  invoices: {
    title: 'Invoices',
    subtitle: 'Raised, paid, open and overdue.',
    count: 'invoices',
    columns: {
      number: 'Number',
      customer: 'Customer',
      status: 'Status',
      issued: 'Issued',
      due: 'Due',
      gross: 'Gross',
      paid: 'Paid on',
      project: 'Project',
    },
    openProject: 'Open project →',
    status: {
      Draft: 'Draft',
      Sent: 'Sent',
      Paid: 'Paid',
      Overdue: 'Overdue',
      Void: 'Voided',
    },
    overdueBadge: 'overdue',
    dueInDays: 'in {0} days',
    overdueByDays: '{0} days late',
  },

  projects: {
    title: 'Projects',
    subtitle: 'Active and completed sites.',
    count: 'projects',
    columns: {
      customer: 'Customer',
      angebot: 'Quote',
      status: 'Status',
      agreed: 'Order value',
      created: 'Started',
      completed: 'Completed',
    },
    status: {
      Active: 'Active',
      OnHold: 'On hold',
      Completed: 'Completed',
    },
  },

  inspections: {
    title: 'Site visits',
    subtitle: 'Scheduled and completed on-site appointments.',
    count: 'appointments',
    columns: {
      when: 'Appointment',
      customer: 'Customer',
      address: 'Address',
      inspector: 'Site management',
      status: 'Status',
    },
    open: 'Open',
    done: 'Done',
    photos: 'Photos',
    rangeThisWeek: 'This week',
    rangeNextWeek: 'Next week',
    rangeMonth: 'Next 30 days',
  },

  /**
   * The seven Lead statuses. `Angebot` is translated to "Quote" here: a status chip must stay
   * readable at a glance, and "Quote (Angebot)" is too long for one.
   */
  /**
   * Delivery status of outgoing email (PermissionMatrix §9). Admin only.
   *
   * Needed because a committed business operation stays successful when its notification fails —
   * and two of the six senders are anonymous public endpoints, where nobody from the company was
   * present to be told.
   */
  notifications: {
    title: 'Notifications',
    subtitle: 'Delivery status of outgoing email.',
    count: 'records',
    columns: {
      type: 'Trigger',
      reference: 'Reference',
      status: 'Status',
      recipient: 'Recipient',
      attempts: 'Attempts',
      created: 'Created',
      lastAttempt: 'Last attempt',
    },
    status: {
      Pending: 'Pending',
      Sending: 'Sending',
      Sent: 'Delivered',
      Failed: 'Failed',
    },
    type: {
      NewWebsiteLead: 'New website enquiry',
      AngebotSubmittedForReview: 'Quote submitted for review',
      AngebotChangesRequested: 'Changes requested on a quote',
      AngebotReady: 'Quote to the customer',
      InvoiceReady: 'Invoice to the customer',
      AngebotDecision: "Customer's decision",
    },
    retry: 'Send again',
    retryTitle: 'Send this notification again?',
    retryBody:
      'Only the email is sent again. The underlying business operation is never repeated. The recipient is resolved afresh.',
    retrySent: 'The notification was delivered.',
    retryFailed: 'Sending failed again. The attempt has been recorded.',
    noRecipient: 'Not resolved',
    sendingHint:
      'A record can be left in “Sending” if an attempt was interrupted. Sending again is then the only way to finish it.',
    failureDetails: 'Error message',
  },

  leadStatus: {
    New: 'New',
    InspectionScheduled: 'Visit scheduled',
    InspectionDone: 'Visit done',
    AngebotInProgress: 'Quote in progress',
    AngebotSent: 'Quote sent',
    Won: 'Won',
    Lost: 'Lost',
  },

  leadSource: {
    Website: 'Website',
    Phone: 'Phone',
    Email: 'Email',
  },

  filters: {
    legend: 'Filters',
    status: 'Status',
    allStatuses: 'All statuses',
    inspector: 'Site management',
    allInspectors: 'All',
    from: 'From',
    to: 'To',
  },

  paging: {
    of: 'of',
    page: 'Page',
    previous: 'Previous',
    next: 'Next',
    showing: '{0}–{1} of {2}',
  },

  formats: {
    date: 'd MMM yyyy',
    dateShort: 'd MMM',
    dateTime: 'd MMM yyyy · HH:mm',
    time: 'HH:mm',
    weekday: 'EEEE, d MMM',
    month: 'LLL',
  },

  language: {
    label: 'Language',
    de: 'DE',
    en: 'EN',
  },

  a11y: {
    statusLabel: 'Status',
    loading: 'Loading content',
  },
};
