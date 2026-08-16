/**
 * Every visible string in the Dashboard, in German — the primary language (D72/D79).
 *
 * One module is where a wording change happens; text is never written inline in a template.
 *
 * **This is not Angular i18n.** No `$localize`, no message extraction, no catalogue, no second
 * build: `$localize` compiles one bundle per locale and cannot switch at runtime, which is exactly
 * what a `DE | EN` toggle in the header needs. A typed dictionary gives runtime switching, no new
 * dependency, *and* compile-time completeness — `en.ts` is annotated with the derived `Strings`
 * type, so a German key with no English translation fails the build.
 *
 * Note also what this file is **not** for: raw backend `ProblemDetails` `detail` text. Server
 * messages are English and phrased for an API caller, so the Dashboard maps a status code — or a
 * `ValidationException`'s field key — onto its own wording here instead of echoing the server's.
 */
export const DE = {
  app: {
    name: 'RenoTrack',
    dashboard: 'RenoTrack Dashboard',
  },

  nav: {
    cockpit: 'Cockpit',
    leads: 'Leads',
    inspections: 'Besichtigungen',
    angebote: 'Angebote',
    invoices: 'Rechnungen',
    projects: 'Projekte',
    catalog: 'Katalog',
    notifications: 'Benachrichtigungen',
    logout: 'Abmelden',
    mainNavigation: 'Hauptnavigation',
    skipToContent: 'Zum Inhalt springen',
    openMenu: 'Menü öffnen',
  },

  roles: {
    admin: 'Verwaltung',
    inspector: 'Bauleitung',
  },

  actions: {
    save: 'Speichern',
    cancel: 'Abbrechen',
    close: 'Schließen',
    retry: 'Erneut versuchen',
    reset: 'Zurücksetzen',
    refresh: 'Aktualisieren',
    open: 'Öffnen',
    login: 'Anmelden',
    showAll: 'Alle anzeigen',
    back: 'Zurück',
    create: 'Anlegen',
    confirm: 'Bestätigen',
    remove: 'Entfernen',
    /** Überschrift der Aktionsspalte — nur für Screenreader, sichtbar bleibt sie leer. */
    actionsColumn: 'Aktionen',
  },

  states: {
    loading: 'Wird geladen …',
    emptyTitle: 'Keine Einträge',
    emptyBody: 'Für die aktuelle Auswahl gibt es nichts anzuzeigen.',
    errorTitle: 'Daten konnten nicht geladen werden',
    errorBody:
      'Bitte erneut versuchen. Besteht das Problem weiterhin, wenden Sie sich an die Administration.',
    noResultsTitle: 'Keine Treffer',
    noResultsBody: 'Passen Sie die Filter an, um mehr Ergebnisse zu sehen.',
  },

  /**
   * Fehlermeldungen nach HTTP-Status. **Niemals der `detail`-Text des Servers** — der ist englisch
   * und für einen API-Aufrufer formuliert, nicht für die Verwaltung eines Handwerksbetriebs.
   */
  errors: {
    validation: 'Die Eingabe ist unvollständig oder ungültig.',
    unauthenticated: 'Die Sitzung ist abgelaufen. Bitte erneut anmelden.',
    forbidden: 'Für diese Aktion fehlt Ihnen die Berechtigung.',
    notFound: 'Der Eintrag wurde nicht gefunden.',
    conflict:
      'Der Vorgang hat sich zwischenzeitlich geändert. Bitte die Seite neu laden und erneut versuchen.',
    gone: 'Dieser Link ist abgelaufen.',
    offline: 'Keine Verbindung zum Server. Bitte die Netzwerkverbindung prüfen.',
    server: 'Auf dem Server ist ein Fehler aufgetreten.',
  },

  login: {
    title: 'Anmelden',
    subtitle: 'Interner Bereich für Verwaltung und Bauleitung.',
    email: 'E-Mail',
    password: 'Passwort',
    submit: 'Anmelden',
    signingIn: 'Wird angemeldet …',
    failed:
      'Anmeldung fehlgeschlagen. Bitte E-Mail-Adresse und Passwort prüfen. Nach mehreren Fehlversuchen wird das Konto vorübergehend gesperrt.',
    emailRequired: 'Bitte eine gültige E-Mail-Adresse eingeben',
    passwordRequired: 'Bitte das Passwort eingeben',
  },

  /** Das Cockpit — die Führungsansicht nach der Anmeldung. */
  cockpit: {
    greetingMorning: 'Guten Morgen',
    greetingDay: 'Guten Tag',
    greetingEvening: 'Guten Abend',
    subtitleAdmin: 'Die Lage des Betriebs auf einen Blick.',
    subtitleInspector: 'Ihre Baustellen, Termine und Angebote.',
    updatedAt: 'Stand',

    kpi: {
      revenue: 'Bezahlt (Jahr)',
      revenueHint: 'Tatsächlich eingegangene Zahlungen.',
      openQuotes: 'Angebotsvolumen offen',
      openQuotesHint: 'Beim Kunden, Entscheidung steht aus.',
      outstanding: 'Offene Forderungen',
      outstandingHint: 'Gestellt und noch nicht bezahlt.',
      overdue: 'Überfällig',
      overdueHint: 'Zahlungsziel überschritten.',
      projects: 'Aktive Projekte',
      projectsHint: 'Baustellen in Ausführung.',
      decisions: 'Entscheidungen',
      decisionsHint: 'Vorgänge warten auf Sie.',

      today: 'Termine heute',
      todayHint: 'Ihre Vor-Ort-Termine.',
      quotesToWrite: 'Angebote zu erstellen',
      quotesToWriteHint: 'Besichtigung erledigt, Angebot fehlt.',
      myQuotes: 'Meine Angebote',
      myQuotesHint: 'In Arbeit oder in Prüfung.',
      week: 'Termine diese Woche',
      weekHint: 'Geplante Besichtigungen.',
    },

    money: {
      heading: 'Rechnungen',
      subheading: 'Gesamtes Rechnungsvolumen des laufenden Jahres.',
      total: 'Gestellt',
      paid: 'Bezahlt',
      open: 'Offen',
      overdue: 'Überfällig',
      voided: 'Storniert',
      empty: 'Noch keine Rechnungen gestellt.',
    },

    funnel: {
      heading: 'Vom Kontakt zum Auftrag',
      subheading: 'Wo Vorgänge liegen bleiben — und wie viel Geld daran hängt.',
      empty: 'Noch keine Anfragen erfasst.',
      stages: {
        New: 'Neue Anfragen',
        InspectionScheduled: 'Besichtigung geplant',
        InspectionDone: 'Besichtigung erledigt',
        AngebotInProgress: 'Angebot in Arbeit',
        AngebotSent: 'Beim Kunden',
        Won: 'Gewonnen',
      },
      conversion: 'Abschlussquote {0} % — {1} von {2} entschiedenen Anfragen gewonnen.',
    },

    decisions: {
      heading: 'Was jetzt ansteht',
      subheading: 'Nach Dringlichkeit. Jeder Eintrag führt direkt zur Arbeit.',
      nothing: 'Nichts Dringendes',
      nothingBody: 'Aktuell wartet kein Vorgang auf Ihre Entscheidung.',
      quotesToApprove: 'Angebote zur Freigabe',
      quotesToApproveHint: 'Warten auf die Verwaltung',
      invoicesOverdue: 'Rechnungen überfällig',
      invoicesOverdueHint: 'Zahlungsziel überschritten',
      awaitingCustomer: 'Kunden ohne Rückmeldung',
      awaitingCustomerHint: 'Angebot versendet, keine Entscheidung',
      leadsToSchedule: 'Besichtigung planen',
      leadsToScheduleHint: 'Neue Anfragen ohne Termin',
      quotesToWrite: 'Angebote zu erstellen',
      quotesToWriteHint: 'Besichtigung erledigt, Angebot fehlt',
      inspectionsToday: 'Besichtigungen heute',
      inspectionsTodayHint: 'Vor-Ort-Termine im Tagesplan',
    },

    schedule: {
      heading: 'Tagesplan',
      subheading: 'Heute und morgen vor Ort.',
      today: 'Heute',
      tomorrow: 'Morgen',
      empty: 'Keine Termine',
      done: 'erledigt',
    },

    projects: {
      heading: 'Laufende Projekte',
      subheading: 'Auftragswert und Status der aktiven Baustellen.',
      empty: 'Keine aktiven Projekte.',
    },
  },

  leads: {
    title: 'Leads',
    subtitleAdmin: 'Alle Anfragen im Überblick.',
    subtitleInspector: 'Ihre zugewiesenen Anfragen.',
    count: 'Anfragen',
    readOnlyHint:
      'Der Status wird nicht hier geändert, sondern durch die jeweilige Aktion im Ablauf.',
    columns: {
      customer: 'Kunde',
      contact: 'Kontakt',
      source: 'Quelle',
      status: 'Status',
      inspector: 'Bauleitung',
      created: 'Eingegangen',
    },
    unassigned: 'Nicht zugewiesen',
    newLead: 'Neuer Lead',
  },

  /** Das Formular für manuelle Anlage (FR-2.1) und für die Korrektur der Kontaktdaten. */
  leadForm: {
    createTitle: 'Neuen Lead anlegen',
    createHint:
      'Für Anfragen, die telefonisch oder per E-Mail eingehen. Anfragen über das Kontaktformular der Website werden automatisch angelegt.',
    editTitle: 'Kontaktdaten korrigieren',
    editHint:
      'Nur die Kontaktdaten werden geändert. Status, Zuweisung und Notizen bleiben unverändert.',
    name: 'Name',
    phone: 'Telefon',
    email: 'E-Mail',
    address: 'Adresse',
    notes: 'Notizen',
    source: 'Quelle',
    optional: 'optional',
    created: 'Lead wurde angelegt.',
    updated: 'Kontaktdaten wurden aktualisiert.',
  },

  angebote: {
    title: 'Angebote',
    subtitle: 'Alle Angebote, ihr Status und ihr Wert.',
    count: 'Angebote',
    columns: {
      number: 'Nummer',
      customer: 'Kunde',
      status: 'Status',
      net: 'Netto',
      gross: 'Brutto',
      created: 'Erstellt',
      sent: 'Versendet',
    },
    totalValue: 'Summe (Brutto)',
    status: {
      Draft: 'Entwurf',
      InReview: 'In Prüfung',
      ChangesRequested: 'Änderungen erbeten',
      ApprovedInternally: 'Intern freigegeben',
      Sent: 'Versendet',
      CustomerApproved: 'Angenommen',
      CustomerRejected: 'Abgelehnt',
    },
  },

  /** Das Angebotsdokument — Wireframes D1 (Erstellung) und D3 (Prüfung). */
  angebotDetail: {
    backToList: 'Zurück zur Angebotsübersicht',
    documentLabel: 'Angebot',
    createdOn: 'erstellt am',
    grossTotal: 'Gesamtsumme (brutto)',
    netTotal: 'Nettobetrag',
    summary: 'Zusammenstellung',
    vatLine: 'zzgl. {0} % MwSt',
    positions: 'Positionen',
    positionsHint: 'Abschnitte und Leistungen dieses Angebots.',
    position: 'Pos.',
    subtotal: 'Zwischensumme',
    emptyTitle: 'Noch keine Positionen',
    emptyBody: 'Legen Sie einen Abschnitt an und fügen Sie darin Leistungen hinzu.',
    emptyBodyRead: 'Für dieses Angebot wurden noch keine Positionen erfasst.',
    sectionEmpty: 'Dieser Abschnitt enthält noch keine Leistungen.',

    addSection: 'Abschnitt hinzufügen',
    addSectionHint: 'Abschnitte gliedern das Angebot, z. B. „Baustelleneinrichtung“.',
    sectionTitle: 'Bezeichnung',
    removeSection: 'Abschnitt entfernen',
    addItem: 'Leistung hinzufügen',
    addItemHint: 'Aus dem Katalog übernehmen oder frei erfassen.',
    specification: 'Beschreibung',
    fromCatalog: 'aus Katalog',
    fromCatalogAction: 'Aus Katalog wählen',
    linkedToCatalog: 'Mit Katalogeintrag verknüpft',
    makeCustom: 'Verknüpfung lösen',
    customItemHint: 'Freie Position — Bezeichnung und Einheit werden hier erfasst.',
    customUnit: 'Eigene Einheit',
    standardUnit: 'Standardeinheit',
    saveToCatalog: 'In Katalog übernehmen',
    required: 'Pflichtfeld',

    columns: {
      description: 'Leistung',
      quantity: 'Menge',
      unit: 'Einheit',
      unitPrice: 'Einzelpreis',
      vat: 'MwSt',
      lineTotal: 'Gesamt',
      actions: 'Aktionen',
    },

    submitForReview: 'Zur Prüfung einreichen',
    requestChanges: 'Änderungen anfordern',
    requestChangesHint:
      'Die Bauleitung erhält Ihren Kommentar und kann das Angebot danach wieder bearbeiten.',
    comment: 'Kommentar',
    approve: 'Freigeben',
    send: 'An Kunden senden',
    convert: 'In Projekt umwandeln',

    reviewHistory: 'Prüfverlauf',
    reviewHistoryHint: 'Alle Rückmeldungen der Verwaltung zu diesem Angebot.',
    noComments: 'Noch keine Rückmeldungen.',
    byAdmin: 'Verwaltung',

    awaitingRework:
      'Die Verwaltung hat Änderungen angefordert. Sobald Sie das Angebot bearbeiten, gilt es wieder als Entwurf und kann erneut eingereicht werden.',
    inReviewHint: 'Dieses Angebot liegt zur Prüfung bei der Verwaltung.',

    sectionAdded: 'Abschnitt hinzugefügt.',
    sectionRemoved: 'Abschnitt entfernt.',
    itemAdded: 'Leistung hinzugefügt.',
    itemRemoved: 'Leistung entfernt.',
    savedToCatalog: 'Position wurde in den Katalog übernommen.',
    submitted: 'Angebot zur Prüfung eingereicht.',
    approved: 'Angebot freigegeben.',
    sent: 'Angebot wurde an den Kunden gesendet.',
    changesRequested: 'Änderungen wurden angefordert.',
    converted: 'Projekt wurde angelegt.',

    duplicate: 'Als Vorlage verwenden',
    duplicateTitle: 'Angebot für eine andere Anfrage übernehmen',
    duplicateHint:
      'Es entsteht ein neuer Entwurf mit denselben Abschnitten und Leistungen. Das ursprüngliche Angebot bleibt unverändert.',
    duplicateTarget: 'Ziel-Anfrage',
    duplicateTargetHint:
      'Es werden Ihre eigenen Anfragen angezeigt. Hat eine Anfrage bereits ein laufendes Angebot, wird die Übernahme abgelehnt.',
    noDuplicateTargets: 'Ihnen ist derzeit keine weitere Anfrage zugewiesen.',
    duplicated: 'Neuer Entwurf wurde aus diesem Angebot erstellt.',

    confirm: {
      submitTitle: 'Angebot einreichen?',
      submitBody:
        'Das Angebot geht zur Prüfung an die Verwaltung und kann bis zur Rückmeldung nicht mehr bearbeitet werden.',
      approveTitle: 'Angebot freigeben?',
      approveBody: 'Nach der Freigabe kann das Angebot an den Kunden gesendet werden.',
      sendTitle: 'Angebot an Kunden senden?',
      sendBody:
        'Der Kunde erhält eine E-Mail mit einem persönlichen Link zur Zusage oder Absage. Dieser Schritt kann nicht rückgängig gemacht werden.',
      convertTitle: 'Projekt anlegen?',
      convertBody:
        'Aus dem angenommenen Angebot wird ein Projekt mit dem vereinbarten Auftragswert erstellt.',
    },
  },

  /** Der Katalog-Auswahldialog (Wireframe D2). */
  catalog: {
    pickerTitle: 'Katalog durchsuchen',
    pickerHint: 'Bezeichnung, Einheit und Beschreibung werden aus dem Katalogeintrag übernommen.',
    search: 'Suchbegriff',
    searchPrompt: 'Geben Sie einen Suchbegriff ein.',
    noResults: 'Keine passenden Katalogeinträge gefunden.',

    // Die Katalogverwaltung (Wireframe F1). Beide Rollen lesen, nur die Verwaltung pflegt.
    title: 'Katalog',
    subtitle: 'Die gemeinsame Leistungsbibliothek für alle Angebote.',
    count: 'Einträge',
    columns: {
      title: 'Bezeichnung',
      specification: 'Beschreibung',
      unit: 'Einheit',
      price: 'Einzelpreis',
      created: 'Angelegt',
    },
    newItem: 'Neuer Eintrag',
    createTitle: 'Katalogeintrag anlegen',
    editTitle: 'Katalogeintrag bearbeiten',
    itemTitle: 'Bezeichnung',
    specification: 'Beschreibung',
    unit: 'Einheit',
    price: 'Einzelpreis (netto)',
    retire: 'Ausmustern',
    retireTitle: 'Katalogeintrag ausmustern?',
    retireBody:
      'Der Eintrag verschwindet aus der Suche und aus dieser Liste. Er wird nicht gelöscht: bestehende Angebote behalten ihre Verknüpfung, und der Eintrag bleibt als direkte Referenz gültig (BR-12, BR-14).',
    itemCreated: 'Katalogeintrag wurde angelegt.',
    itemUpdated: 'Katalogeintrag wurde aktualisiert.',
    itemRetired: 'Katalogeintrag wurde ausgemustert.',
    editHint:
      'Änderungen gelten nur für neue Angebote. Bereits erstellte Angebotspositionen bleiben unverändert (BR-8).',
    readOnlyHint: 'Der Katalog wird von der Verwaltung gepflegt.',
    contributeHint:
      'Eigene Positionen übernehmen Sie direkt aus einem Angebot in den Katalog.',
  },

  /** Die Lead-Detailseite (Wireframe C1) mit den beiden Aktionen, die den Vorgang bewegen. */
  leadDetail: {
    backToPipeline: 'Zurück zur Übersicht',
    label: 'Anfrage',
    contact: 'Kontakt',
    phone: 'Telefon',
    email: 'E-Mail',
    address: 'Adresse',
    inspector: 'Bauleitung',
    notes: 'Notizen',
    scheduleInspection: 'Besichtigung planen',
    scheduleHint:
      'Der gewählten Bauleitung wird die Anfrage gleichzeitig zugewiesen (BR-13).',
    dateTime: 'Termin',
    chooseInspector: 'Bitte auswählen',
    createAngebot: 'Angebot erstellen',
    angebote: 'Angebote zu dieser Anfrage',
    angeboteHint: 'Alle Angebote, die für diese Anfrage erstellt wurden.',
    noAngebote: 'Für diese Anfrage wurde noch kein Angebot erstellt.',
    scheduled: 'Besichtigung wurde geplant.',
    angebotCreated: 'Angebotsentwurf wurde angelegt.',

    editContact: 'Kontaktdaten bearbeiten',
    assignInspector: 'Bauleitung zuweisen',
    changeInspector: 'Bauleitung ändern',
    assignTitle: 'Bauleitung zuweisen',
    assignHint:
      'Die zugewiesene Bauleitung sieht diese Anfrage in ihrer eigenen Übersicht. Der Status der Anfrage ändert sich dadurch nicht.',
    assigned: 'Bauleitung wurde zugewiesen.',
  },

  /** Die Vor-Ort-Ansicht (Wireframe C3) — der einzige bewusst mobil-zuerst gebaute Bildschirm. */
  inspectionDetail: {
    backToSchedule: 'Zurück zum Terminplan',
    label: 'Besichtigung',
    onSite: 'Vor Ort',
    openLead: 'Anfrage öffnen',
    photosHint: 'Aufnahmen dokumentieren den Zustand vor Beginn der Arbeiten.',
    addPhoto: 'Foto hochladen',
    markComplete: 'Besichtigung abschließen',
    confirmTitle: 'Besichtigung abschließen?',
    confirmBody:
      'Danach können weder Fotos noch Notizen ergänzt werden (BR-10). Die Anfrage ist anschließend bereit für ein Angebot.',
    completedHint:
      'Diese Besichtigung ist abgeschlossen und kann nicht mehr verändert werden.',
    notesSaved: 'Notizen wurden gespeichert.',
    photoUploaded: 'Foto wurde hochgeladen.',
    completed: 'Besichtigung wurde abgeschlossen.',

    reassign: 'Bauleitung ändern',
    reassignTitle: 'Besichtigung übertragen',
    reassignHint:
      'Die Anfrage wird derselben Bauleitung zugewiesen (BR-13). Nach dem Abschluss ist eine Übertragung nicht mehr möglich.',
    reassigned: 'Besichtigung wurde übertragen.',
  },

  /** Die Projekt-Detailseite mit dem Rechnungsablauf (Wireframes E1–E3). */
  projectDetail: {
    backToList: 'Zurück zur Projektübersicht',
    label: 'Projekt',
    fromAngebot: 'aus Angebot',
    originatingLead: 'Anfrage öffnen',
    agreed: 'Auftragswert',
    invoiced: 'Bereits berechnet',
    remaining: 'Offen zu berechnen',
    overInvoiced:
      'Es wurde mehr berechnet als vereinbart. Das ist zulässig, sollte aber geprüft werden (BR-3).',

    invoices: 'Rechnungen',
    invoicesHint: 'Alle Rechnungen dieses Projekts, einschließlich stornierter.',
    noInvoices: 'Für dieses Projekt wurde noch keine Rechnung gestellt.',
    addInvoice: 'Rechnung anlegen',
    addInvoiceHint:
      'Der Betrag wird anhand der Mehrwertsteuersätze des Angebots automatisch aufgeteilt.',
    grossAmount: 'Betrag (brutto)',
    dueDate: 'Fällig am',

    send: 'Senden',
    sendInvoiceTitle: 'Rechnung an Kunden senden?',
    sendInvoiceBody:
      'Der Kunde erhält eine E-Mail mit einem persönlichen Link zur Rechnung. Dieser Schritt kann nicht rückgängig gemacht werden.',
    markPaid: 'Als bezahlt buchen',
    markPaidHint: 'Es wird immer der volle Rechnungsbetrag gebucht.',
    paidAt: 'Zahlungseingang am',
    method: 'Zahlungsart',
    void: 'Stornieren',
    voidTitle: 'Rechnung stornieren?',
    voidHint:
      'Die Rechnung bleibt mit ihrer Nummer erhalten und wird als storniert gekennzeichnet (BR-9).',
    voidReason: 'Grund',

    completeProject: 'Projekt abschließen',
    completed: 'Projekt wurde abgeschlossen.',
    overrideTitle: 'Projekt trotz offener Rechnungen abschließen?',
    overrideBody:
      'Es gibt noch offene oder gar keine Rechnungen. Der Abschluss ist mit Begründung dennoch möglich; die Begründung wird protokolliert.',
    overrideReason: 'Begründung',
    completeAnyway: 'Trotzdem abschließen',

    invoiceCreated: 'Rechnung wurde angelegt.',
    invoiceSent: 'Rechnung wurde an den Kunden gesendet.',
    invoicePaid: 'Zahlung wurde gebucht.',
    invoiceVoided: 'Rechnung wurde storniert.',

    putOnHold: 'Projekt pausieren',
    holdTitle: 'Projekt pausieren?',
    holdBody:
      'Die Arbeiten ruhen vorübergehend, etwa bis Material eintrifft. Rechnungen bleiben davon unberührt und können weiterhin gestellt werden.',
    resume: 'Projekt fortsetzen',
    resumeTitle: 'Projekt fortsetzen?',
    resumeBody: 'Das Projekt wird wieder als laufend geführt.',
    heldMessage: 'Projekt wurde pausiert.',
    resumedMessage: 'Projekt wurde fortgesetzt.',
    onHoldHint:
      'Dieses Projekt ruht. Ein Abschluss ist erst nach dem Fortsetzen möglich.',
  },

  paymentMethod: {
    BankTransfer: 'Überweisung',
    Cash: 'Barzahlung',
    Other: 'Sonstige',
  },

  invoices: {
    title: 'Rechnungen',
    subtitle: 'Gestellt, bezahlt, offen und überfällig.',
    count: 'Rechnungen',
    columns: {
      number: 'Nummer',
      customer: 'Kunde',
      status: 'Status',
      issued: 'Gestellt',
      due: 'Fällig',
      gross: 'Brutto',
      paid: 'Bezahlt am',
      project: 'Projekt',
    },
    openProject: 'Zum Projekt →',
    status: {
      Draft: 'Entwurf',
      Sent: 'Versendet',
      Paid: 'Bezahlt',
      Overdue: 'Überfällig',
      Void: 'Storniert',
    },
    overdueBadge: 'überfällig',
    dueInDays: 'in {0} Tagen',
    overdueByDays: 'seit {0} Tagen',
  },

  projects: {
    title: 'Projekte',
    subtitle: 'Aktive und abgeschlossene Baustellen.',
    count: 'Projekte',
    columns: {
      customer: 'Kunde',
      angebot: 'Angebot',
      status: 'Status',
      agreed: 'Auftragswert',
      created: 'Angelegt',
      completed: 'Abgeschlossen',
    },
    status: {
      Active: 'Aktiv',
      OnHold: 'Pausiert',
      Completed: 'Abgeschlossen',
    },
  },

  inspections: {
    title: 'Besichtigungen',
    subtitle: 'Geplante und erledigte Vor-Ort-Termine.',
    count: 'Termine',
    columns: {
      when: 'Termin',
      customer: 'Kunde',
      address: 'Adresse',
      inspector: 'Bauleitung',
      status: 'Status',
    },
    open: 'Offen',
    done: 'Erledigt',
    photos: 'Fotos',
    rangeThisWeek: 'Diese Woche',
    rangeNextWeek: 'Nächste Woche',
    rangeMonth: 'Nächste 30 Tage',
  },

  /** Die sieben Lead-Status (`StateMachine.md` §1.1) — vollständig, sonst Compile-Fehler. */
  /**
   * Der Versandstatus ausgehender E-Mails (PermissionMatrix §9). Nur für die Verwaltung.
   *
   * Nötig, weil ein abgeschlossener Geschäftsvorgang auch dann gültig bleibt, wenn seine
   * Benachrichtigung fehlschlägt — und zwei der sechs Absender sind anonyme öffentliche
   * Endpunkte, bei denen niemand aus dem Haus anwesend war, dem man es hätte sagen können.
   */
  notifications: {
    title: 'Benachrichtigungen',
    subtitle: 'Versandstatus der ausgehenden E-Mails.',
    count: 'Vorgänge',
    columns: {
      type: 'Anlass',
      reference: 'Bezug',
      status: 'Status',
      recipient: 'Empfänger',
      attempts: 'Versuche',
      created: 'Angelegt',
      lastAttempt: 'Letzter Versuch',
    },
    status: {
      Pending: 'Ausstehend',
      Sending: 'Wird gesendet',
      Sent: 'Zugestellt',
      Failed: 'Fehlgeschlagen',
    },
    type: {
      NewWebsiteLead: 'Neue Anfrage über die Website',
      AngebotSubmittedForReview: 'Angebot zur Prüfung eingereicht',
      AngebotChangesRequested: 'Änderungen am Angebot erbeten',
      AngebotReady: 'Angebot an Kunden',
      InvoiceReady: 'Rechnung an Kunden',
      AngebotDecision: 'Entscheidung des Kunden',
    },
    retry: 'Erneut senden',
    retryTitle: 'Benachrichtigung erneut senden?',
    retryBody:
      'Es wird ausschließlich die E-Mail erneut versendet. Der zugrunde liegende Geschäftsvorgang wird nicht wiederholt. Der Empfänger wird dabei neu ermittelt.',
    retrySent: 'Benachrichtigung wurde zugestellt.',
    retryFailed: 'Der Versand ist erneut fehlgeschlagen. Der Vorgang wurde vermerkt.',
    noRecipient: 'Nicht ermittelt',
    sendingHint:
      'Ein Vorgang kann in „Wird gesendet“ hängen bleiben, wenn ein Versuch abgebrochen wurde. Ein erneuter Versand ist dann der einzige Weg, ihn abzuschließen.',
    failureDetails: 'Fehlermeldung',
  },

  leadStatus: {
    New: 'Neu',
    InspectionScheduled: 'Besichtigung geplant',
    InspectionDone: 'Besichtigung erledigt',
    AngebotInProgress: 'Angebot in Arbeit',
    AngebotSent: 'Angebot versendet',
    Won: 'Gewonnen',
    Lost: 'Verloren',
  },

  leadSource: {
    Website: 'Website',
    Phone: 'Telefon',
    Email: 'E-Mail',
  },

  filters: {
    legend: 'Filter',
    status: 'Status',
    allStatuses: 'Alle Status',
    inspector: 'Bauleitung',
    allInspectors: 'Alle',
    from: 'Von',
    to: 'Bis',
  },

  paging: {
    of: 'von',
    page: 'Seite',
    previous: 'Zurück',
    next: 'Weiter',
    showing: '{0}–{1} von {2}',
  },

  /**
   * Datums-/Zeitmuster je Sprache.
   *
   * Im Wörterbuch, weil **das Muster selbst** sich unterscheidet, nicht nur die Monatsnamen: Deutsch
   * schreibt `04.08.2026`, Englisch `4 Aug 2026`. Die aktive Locale an die Pipe zu geben übersetzt
   * nur die Wörter; erst ein eigenes Muster ändert Reihenfolge und Trennzeichen (D79).
   *
   * **Geld fehlt hier bewusst.** Die Währung ist immer EUR und wird von `CurrencyPipe` aus der
   * aktiven Locale formatiert — der Wert ändert sich nie mit der Sprache, nur seine Darstellung.
   */
  formats: {
    date: 'dd.MM.yyyy',
    dateShort: 'dd.MM.',
    dateTime: 'dd.MM.yyyy · HH:mm',
    time: 'HH:mm',
    weekday: 'EEEE, dd.MM.',
    month: 'LLL',
  },

  language: {
    label: 'Sprache',
    de: 'DE',
    en: 'EN',
  },

  a11y: {
    statusLabel: 'Status',
    loading: 'Inhalt wird geladen',
  },
} as const;
