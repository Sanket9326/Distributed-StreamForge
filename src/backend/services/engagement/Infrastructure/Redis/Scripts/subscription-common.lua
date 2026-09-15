local function decode_state(raw)
  local state = cjson.decode(raw)
  decimal(state.sourceOffset)
  if type(state.sourcePartition) ~= 'number' or state.sourcePartition < 0 or
    state.sourcePartition % 1 ~= 0 or compare(state.sourceOffset, '9223372036854775807') > 0 or
    type(state.isActive) ~= 'boolean' or type(state.confirmed) ~= 'boolean' or
    type(state.sortTime) ~= 'string' or #state.sortTime ~= 19 or not string.match(state.sortTime, '^%d+$') or
    type(state.createdAtUtc) ~= 'string' or type(state.updatedAtUtc) ~= 'string' then error('INVALID_STATE') end
  return state
end
local function choose(current, incoming)
  if not current then return incoming end
  if current.sourcePartition ~= incoming.sourcePartition then error('PARTITION_CHANGED') end
  local order = compare(current.sourceOffset, incoming.sourceOffset)
  if order > 0 or (order == 0 and current.confirmed and not incoming.confirmed) then return current end
  if not incoming.confirmed and incoming.isActive and current.isActive then
    incoming.createdAtUtc = current.createdAtUtc; incoming.sortTime = current.sortTime
  end
  return incoming
end
local function write_state(stateKey, orderKey, id, old, state)
  if old then redis.call('ZREM', orderKey, old.sortTime .. ':' .. string.gsub(id, '-', '')) end
  redis.call('HSET', stateKey, id, cjson.encode(state))
  if state.isActive then redis.call('ZADD', orderKey, 0, state.sortTime .. ':' .. string.gsub(id, '-', '')) end
end
