import {
  reactExtension,
  useApi,
  BlockStack,
  InlineStack,
  Text,
  Heading,
  Button,
  Divider,
  Badge,
  Banner,
  Link,
} from '@shopify/ui-extensions-react/admin';
import { useState, useEffect, useCallback } from 'react';

const BACKEND_URL = 'https://eboni-unprizable-discriminatingly.ngrok-free.dev';

function App() {
  const api = useApi('admin.order-details.block.render');
  const [orderId, setOrderId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [orderData, setOrderData] = useState(null);
  const [idToken, setIdToken] = useState(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [successMsg, setSuccessMsg] = useState(null);
  // Track if payment was just processed (so Pay button hides even if status didn't update yet)
  const [paymentDone, setPaymentDone] = useState(false);
  // Track if we should show all tracking events (or default to 3 to keep height low)
  const [showAllEvents, setShowAllEvents] = useState(false);

  // Background token refresh interval (keep OIDC token fresh for download links)
  useEffect(() => {
    if (!orderId) return;
    const interval = setInterval(async () => {
      try {
        const token = await api.auth.idToken();
        setIdToken(token);
      } catch (_) {}
    }, 30000); // 30 seconds
    return () => clearInterval(interval);
  }, [orderId, api.auth]);

  useEffect(() => {
    const selected = api.data && api.data.selected;
    if (selected && selected.length > 0) {
      const fullId = selected[0].id;
      const numericId = fullId.split('/').pop();
      setOrderId(numericId);
    }
  }, [api.data]);

  useEffect(() => {
    if (orderId) {
      fetchOrderDetails();
    }
  }, [orderId]);

  async function getToken() {
    try {
      const token = await api.auth.idToken();
      setIdToken(token);
      return token;
    } catch (e) {
      return null;
    }
  }

  function makeHeaders(token) {
    const headers = { 'ngrok-skip-browser-warning': '69420' };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    return headers;
  }

  async function fetchOrderDetails() {
    setLoading(true);
    setError(null);
    try {
      const token = await getToken();
      const response = await fetch(
        `${BACKEND_URL}/api/shopify/order-actions/details?orderId=${orderId}`,
        { headers: makeHeaders(token) }
      );
      const result = await response.json();
      if (response.ok && result.success) {
        setOrderData(result.order);
      } else {
        setError(result.message || 'No SkyPoint booking found for this order.');
      }
    } catch (err) {
      setError(`Failed to connect to backend: ${err.message}`);
    } finally {
      setLoading(false);
    }
  }

  async function handlePayWithWallet() {
    if (actionLoading) return;
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const token = await getToken();
      const response = await fetch(
        `${BACKEND_URL}/api/shopify/order-actions/pay?orderId=${orderId}`,
        { method: 'POST', headers: makeHeaders(token) }
      );
      const result = await response.json();
      if (response.ok && result.success) {
        setPaymentDone(true);
        setSuccessMsg('Payment processed successfully! Waybill created.');
        // Always re-fetch fresh order data after payment
        await refreshOrder(token);
      } else {
        setError(result.message || 'Wallet payment failed.');
      }
    } catch (err) {
      setError(`Connection error: ${err.message}`);
    } finally {
      setActionLoading(false);
    }
  }

  async function handleSyncTracking() {
    if (actionLoading) return;
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const token = await getToken();
      const response = await fetch(
        `${BACKEND_URL}/api/shopify/order-actions/sync?orderId=${orderId}`,
        { method: 'POST', headers: makeHeaders(token) }
      );
      const result = await response.json();
      if (response.ok && result.success) {
        setSuccessMsg('Tracking synced successfully!');
        // Always re-fetch fresh order data after sync
        await refreshOrder(token);
      } else {
        setError(result.message || 'Failed to sync tracking.');
      }
    } catch (err) {
      setError(`Connection error: ${err.message}`);
    } finally {
      setActionLoading(false);
    }
  }

  async function handleDownloadWaybill() {
    if (actionLoading) return;
    setActionLoading(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const token = await getToken();
      const response = await fetch(
        `${BACKEND_URL}/api/shopify/order-actions/waybill/download?orderId=${orderId}`,
        { method: 'GET', headers: makeHeaders(token) }
      );
      
      if (!response.ok) {
        const errResult = await response.json().catch(() => ({}));
        throw new Error(errResult.message || 'Waybill download failed.');
      }

      const result = await response.json();
      if (result && result.fileStream) {
        const binaryString = window.atob(result.fileStream);
        const len = binaryString.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) {
          bytes[i] = binaryString.charCodeAt(i);
        }
        
        const blob = new Blob([bytes], { type: result.applicationType || 'application/pdf' });
        const url = window.URL.createObjectURL(blob);
        
        const a = document.createElement('a');
        a.style.display = 'none';
        a.href = url;
        a.download = result.fileName || `waybill_${orderId}.pdf`;
        document.body.appendChild(a);
        a.click();
        
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);
        
        setSuccessMsg('Waybill PDF downloaded successfully!');
      } else {
        setError('Waybill not found or download failed.');
      }
    } catch (err) {
      setError(`Download error: ${err.message}`);
    } finally {
      setActionLoading(false);
    }
  }

  // Re-fetch order details with existing token (no new token needed)
  async function refreshOrder(token) {
    try {
      const response = await fetch(
        `${BACKEND_URL}/api/shopify/order-actions/details?orderId=${orderId}`,
        { headers: makeHeaders(token) }
      );
      const result = await response.json();
      if (response.ok && result.success) {
        setOrderData(result.order);
      }
    } catch (_) {
      // Ignore refresh errors — user still sees success message
    }
  }

  // ── Render states ────────────────────────────────────────────────────────────

  if (loading) {
    return (
      <BlockStack>
        <Text>Loading SkyPoint shipping details...</Text>
      </BlockStack>
    );
  }

  if (error && !orderData) {
    return (
      <BlockStack>
        <Heading>SkyPoint Shipping</Heading>
        <Banner tone="critical" title={error} />
        <Button onPress={fetchOrderDetails}>Retry</Button>
      </BlockStack>
    );
  }

  if (!orderData) {
    return (
      <BlockStack>
        <Heading>SkyPoint Shipping</Heading>
        <Text>No SkyPoint booking found for this order.</Text>
      </BlockStack>
    );
  }

  // Helper to get property in either PascalCase or camelCase
  const getProp = (propName) => {
    if (!orderData) return undefined;
    if (orderData[propName] !== undefined) return orderData[propName];
    const camel = propName.charAt(0).toLowerCase() + propName.slice(1);
    if (orderData[camel] !== undefined) return orderData[camel];
    return undefined;
  };

  // ── Data (safe casing checking) ──────────────────────────────────────────────
  const toCounterCode = getProp('ToCounterCode');
  const toCounterName = getProp('ToCounterName');
  const pudoAddress1 = getProp('PudoAddress1');
  const pudoCity = getProp('PudoCity');
  const pudoZip = getProp('PudoZip');

  const isPudo = Boolean(toCounterCode);
  const rawStatus = getProp('SkypointStatus') || '';
  const normalizedStatus = rawStatus.toUpperCase().replace(/\s+/g, '_');

  const trackNo = getProp('SkypointTrackNo') ? String(getProp('SkypointTrackNo')) : null;
  const waybillNo = getProp('SkypointWaybillNo') ? String(getProp('SkypointWaybillNo')) : null;

  const isPaid = getProp('IsPaid') || false;

  // Pay: only show if NOT paid (both local state and backend property are false)
  const showPay = !paymentDone && !isPaid;

  // Download Waybill: show when paid (either local paymentDone state or backend isPaid property is true)
  const showDownload = paymentDone || isPaid;

  // Sync Tracking: show when we have either a track or waybill number
  const showSync = Boolean(trackNo) || Boolean(waybillNo);

  const isWaybillGenerating = (paymentDone || isPaid) && !waybillNo;

  const downloadUrl = `${BACKEND_URL}/api/shopify/order-actions/waybill/download-pdf?orderId=${orderId}&token=${encodeURIComponent(idToken || '')}`;

  // Badge tone
  let statusTone = 'info';
  if (!isPaid && !paymentDone) {
    statusTone = 'warning';
  } else if (normalizedStatus === 'CANCELLED') {
    statusTone = 'critical';
  } else if (
    normalizedStatus === 'CREATED_WAYBILL' ||
    normalizedStatus === 'GENERATED_WAYBILL' ||
    normalizedStatus === 'DELIVERED' ||
    normalizedStatus === 'POD_IMAGE_SCANNED' ||
    normalizedStatus === '3RD_PARTY_POD_RECEIVED'
  ) {
    statusTone = 'success';
  }

  const statusLabel = isPaid || paymentDone
    ? (rawStatus ? rawStatus.replace(/_/g, ' ') : 'Paid')
    : 'Awaiting Payment';

  // TrackingHistory is PascalCase (matches C# property name, no camelCase overwrite)
  const rawHistory = getProp('TrackingHistory');
  const history = Array.isArray(rawHistory) ? rawHistory : [];
  const displayedHistory = showAllEvents ? history : history.slice(0, 3);

  return (
    <BlockStack>
      <Heading>SkyPoint Shipping &amp; Tracking</Heading>

      {successMsg ? <Banner tone="success" title={successMsg} /> : null}
      {error ? <Banner tone="critical" title={error} /> : null}
      {isWaybillGenerating ? (
        <Banner
          tone="info"
          title="Waybill barcode is being generated by SkyNet. This may take a few minutes in UAT. Please wait and click 'Sync Tracking Info' later."
        />
      ) : null}

      {/* ── Order summary ── */}
      <BlockStack>
        <InlineStack inlineAlignment="start">
          <Text fontWeight="bold">Booking ID:</Text>
          <Text>{trackNo || '-'}</Text>
        </InlineStack>

        {waybillNo ? (
          <InlineStack inlineAlignment="start">
            <Text fontWeight="bold">Waybill No:</Text>
            <Text>{waybillNo}</Text>
          </InlineStack>
        ) : null}

        <InlineStack inlineAlignment="start" blockAlignment="center">
          <Text fontWeight="bold">Status:</Text>
          <Badge tone={statusTone}>{statusLabel}</Badge>
        </InlineStack>
      </BlockStack>

      <Divider />

      {/* ── PUDO counter info ── */}
      {isPudo ? (
        <BlockStack>
          <Text fontWeight="bold">PUDO Counter:</Text>
          <Text>
            {String(toCounterName || '')} ({String(toCounterCode || '')})
          </Text>
          <Text>
            {String(pudoAddress1 || '')}, {String(pudoCity || '')},{' '}
            {String(pudoZip || '')}
          </Text>
          <Divider />
        </BlockStack>
      ) : null}

      {/* ── Action buttons ── */}
      <BlockStack>
        {showPay ? (
          <Button onPress={handlePayWithWallet} variant="primary">
            {actionLoading ? 'Processing...' : 'Pay With Wallet'}
          </Button>
        ) : null}

        {showDownload ? (
          isWaybillGenerating ? (
            <Button
              variant="secondary"
              disabled={true}
            >
              Generating Barcode...
            </Button>
          ) : (
            <Link href={downloadUrl} external={true}>
              <Button variant="secondary">
                Download Waybill PDF
              </Button>
            </Link>
          )
        ) : null}

        {showSync ? (
          <Button onPress={handleSyncTracking} variant="secondary">
            {actionLoading ? 'Syncing...' : 'Sync Tracking Info'}
          </Button>
        ) : null}
      </BlockStack>

      {/* ── Tracking Timeline ── */}
      {history.length > 0 ? (
        <BlockStack>
          <Divider />
          <InlineStack inlineAlignment="space-between" blockAlignment="center">
            <Text fontWeight="bold">Tracking Timeline:</Text>
            {history.length > 3 ? (
              <Link onPress={() => setShowAllEvents(!showAllEvents)}>
                {showAllEvents ? 'Show less' : `Show all (${history.length})`}
              </Link>
            ) : null}
          </InlineStack>
          <BlockStack>
            {displayedHistory.map((ev, index) => {
              const eventDate = String(ev.WaybillEventDate || '');
              const eventTime = String(ev.WaybillEventTime || '');
              const eventBranch = String(ev.WaybillEventBranch || '');
              const eventDesc = String(ev.WaybillEventDescription || '');

              const parts = [
                eventDate,
                eventTime,
                eventBranch ? `• ${eventBranch}` : '',
              ].filter(Boolean);
              const dateStr = parts.join(' ');

              return (
                <BlockStack key={index}>
                  {dateStr ? <Text>{dateStr}</Text> : null}
                  {eventDesc ? <Text fontWeight="bold">{eventDesc}</Text> : null}
                  {index < displayedHistory.length - 1 ? <Divider /> : null}
                </BlockStack>
              );
            })}
          </BlockStack>
        </BlockStack>
      ) : null}
    </BlockStack>
  );
}

export default reactExtension('admin.order-details.block.render', () => <App />);