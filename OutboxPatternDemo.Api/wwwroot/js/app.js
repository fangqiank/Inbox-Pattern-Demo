const API = '';

async function api(path, options = {}) {
    const res = await fetch(API + path, {
        headers: { 'Content-Type': 'application/json' },
        ...options,
    });
    if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || res.statusText);
    }
    return res.json();
}

function showMsg(el, text, type) {
    el.textContent = text;
    el.className = 'msg ' + type;
    if (type === 'ok') setTimeout(() => { el.textContent = ''; el.className = 'msg'; }, 3000);
}

function esc(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
}

function fmtTime(iso) {
    return new Date(iso).toLocaleTimeString();
}

function fmtMs(ms) {
    return ms == null ? '-' : ms.toFixed(0) + 'ms';
}

// ========== Users ==========

async function loadUsers() {
    const users = await api('/api/users');
    const list = document.getElementById('user-list');
    const followerSel = document.getElementById('follower-select');
    const followedSel = document.getElementById('followed-select');

    if (users.length === 0) {
        list.className = 'list empty';
        list.textContent = 'No users yet. Create one above.';
        followerSel.innerHTML = '<option value="">Follower</option>';
        followedSel.innerHTML = '<option value="">Followed</option>';
        return;
    }

    list.className = 'list';
    list.innerHTML = users.map(u =>
        `<div class="list-item">
            <span class="name">${esc(u.username)}</span>
            <span class="meta">${u.id.slice(0, 8)}...</span>
        </div>`
    ).join('');

    const opts = users.map(u => `<option value="${u.id}">${esc(u.username)}</option>`).join('');

    // preserve current selections
    const prevFollower = followerSel.value;
    const prevFollowed = followedSel.value;

    followerSel.innerHTML = '<option value="">Follower</option>' + opts;
    followedSel.innerHTML = '<option value="">Followed</option>' + opts;

    followerSel.value = prevFollower;
    followedSel.value = prevFollowed;
}

document.getElementById('create-user-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const input = document.getElementById('username-input');
    const msg = document.getElementById('create-user-msg');
    const name = input.value.trim();
    if (!name) return;

    try {
        await api('/api/users', { method: 'POST', body: JSON.stringify({ username: name }) });
        input.value = '';
        showMsg(msg, `User "${name}" created.`, 'ok');
        await loadUsers();
    } catch (err) {
        showMsg(msg, err.message, 'err');
    }
});

document.getElementById('follow-btn').addEventListener('click', async () => {
    const followerId = document.getElementById('follower-select').value;
    const followedId = document.getElementById('followed-select').value;
    const msg = document.getElementById('follow-msg');

    if (!followerId || !followedId) {
        showMsg(msg, 'Please select both users.', 'err');
        return;
    }
    if (followerId === followedId) {
        showMsg(msg, 'Cannot follow yourself.', 'err');
        return;
    }

    try {
        const result = await api(`/api/users/${followerId}/follow/${followedId}`, { method: 'POST' });
        showMsg(msg, result.message, 'ok');
    } catch (err) {
        showMsg(msg, err.message, 'err');
    }
});

// ========== Outbox ==========

function outboxTag(m) {
    if (m.processedOnUtc) return { cls: 'tag-processed', text: 'Processed' };
    if (m.error) return { cls: 'tag-error', text: 'Error' };
    return { cls: 'tag-pending', text: 'Pending' };
}

async function loadOutbox() {
    const messages = await api('/api/outbox/messages');
    const list = document.getElementById('outbox-list');

    if (messages.length === 0) {
        list.className = 'list empty';
        list.textContent = 'No outbox messages. Follow a user to trigger one.';
        return;
    }

    list.className = 'list';
    list.innerHTML = messages.map(m => {
        const tag = outboxTag(m);
        return `<div class="list-item clickable" data-message-id="${m.messageId}" onclick="showMessageDetail(this)">
            <div class="left">
                <span class="tag ${tag.cls}">${tag.text}</span>
                <span class="name">${esc(m.name)}</span>
            </div>
            <span class="meta">${fmtTime(m.createdOnUtc)}</span>
        </div>`;
    }).join('');
}

document.getElementById('refresh-outbox').addEventListener('click', loadOutbox);

// ========== Inbox ==========

function inboxTag(m) {
    if (m.processedOnUtc && !m.error) return { cls: 'tag-processed', text: 'Processed' };
    if (m.error) return { cls: 'tag-error', text: 'Error' };
    return { cls: 'tag-pending', text: 'Pending' };
}

async function loadInbox() {
    const messages = await api('/api/inbox/messages');
    const list = document.getElementById('inbox-list');

    if (messages.length === 0) {
        list.className = 'list empty';
        list.textContent = 'No inbox messages yet.';
        return;
    }

    list.className = 'list';
    list.innerHTML = messages.map(m => {
        const tag = inboxTag(m);
        return `<div class="list-item">
            <div class="left">
                <span class="tag ${tag.cls}">${tag.text}</span>
                <span class="name">${esc(m.handlerName || m.name)}</span>
            </div>
            <span class="meta">${m.processedOnUtc ? fmtTime(m.processedOnUtc) : '--'}</span>
        </div>`;
    }).join('');
}

document.getElementById('refresh-inbox').addEventListener('click', loadInbox);

// ========== Stats ==========

async function loadStats() {
    const stats = await api('/api/inbox/stats');
    const container = document.getElementById('stats-container');

    if (stats.length === 0) {
        container.innerHTML = '<div class="stats-empty">No processing stats yet.</div>';
        return;
    }

    container.innerHTML = `<table class="stats-table">
        <thead>
            <tr>
                <th>Event</th>
                <th>Handler</th>
                <th>Total</th>
                <th>Failed</th>
                <th>Avg Time</th>
            </tr>
        </thead>
        <tbody>
            ${stats.map(s => `<tr>
                <td>${esc(s.eventType)}</td>
                <td>${esc(s.handler)}</td>
                <td class="stat-num">${s.total}</td>
                <td class="stat-num ${s.failed > 0 ? 'stat-failed' : ''}">${s.failed}</td>
                <td class="stat-time stat-num">${fmtMs(s.averageProcessingTime)}</td>
            </tr>`).join('')}
        </tbody>
    </table>`;
}

document.getElementById('refresh-stats').addEventListener('click', loadStats);

// ========== Message Detail Modal ==========

async function showMessageDetail(el) {
    const messageId = el.dataset.messageId;
    const overlay = document.getElementById('modal-overlay');
    const title = document.getElementById('modal-title');
    const body = document.getElementById('modal-body');

    title.textContent = `Message ${messageId.slice(0, 8)}...`;
    body.innerHTML = '<div class="stats-empty">Loading...</div>';
    overlay.classList.remove('hidden');

    try {
        const handlers = await api(`/api/inbox/messages/${messageId}`);
        if (handlers.length === 0) {
            body.innerHTML = '<div class="stats-empty">No handler records yet (message may still be pending).</div>';
            return;
        }

        body.innerHTML = handlers.map(h => {
            const status = h.processingTime != null
                ? `<span class="tag tag-processed">Done ${fmtMs(h.processingTime)}</span>`
                : `<span class="tag tag-pending">Pending</span>`;
            return `<div class="handler-item">
                <div style="display:flex;justify-content:space-between;align-items:center">
                    <span class="handler-name">${esc(h.handlerName)}</span>
                    ${status}
                </div>
                ${h.processedOnUtc ? `<div class="handler-time">Completed: ${fmtTime(h.processedOnUtc)}</div>` : ''}
            </div>`;
        }).join('');
    } catch (err) {
        body.innerHTML = `<div class="stats-empty stat-failed">${esc(err.message)}</div>`;
    }
}

function closeModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

document.getElementById('modal-close').addEventListener('click', closeModal);
document.getElementById('modal-overlay').addEventListener('click', (e) => {
    if (e.target === e.currentTarget) closeModal();
});

// ========== Init ==========

async function refreshAll() {
    try {
        await Promise.all([loadUsers(), loadOutbox(), loadInbox(), loadStats()]);
    } catch (err) {
        console.error('Refresh failed:', err);
    }
}

refreshAll();

// Auto-refresh every 5 seconds
setInterval(refreshAll, 5000);
