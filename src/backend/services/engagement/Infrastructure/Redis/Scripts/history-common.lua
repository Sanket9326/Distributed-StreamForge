local function history_decode(raw)
  local s = cjson.decode(raw)
  decimal(s.sourceOffset)
  decimal(s.positionMs); decimal(s.durationMs)
  if type(s.sourcePartition) ~= 'number' or s.sourcePartition < 0 or s.sourcePartition % 1 ~= 0 or
    compare(s.sourceOffset, '9223372036854775807') > 0 or type(s.confirmed) ~= 'boolean' or
    type(s.sortTime) ~= 'string' or #s.sortTime ~= 19 or not string.match(s.sortTime, '^%d+$') or
    type(s.createdAtUtc) ~= 'string' or type(s.updatedAtUtc) ~= 'string' or
    s.durationMs == '0' or compare(s.positionMs, s.durationMs) > 0 or compare(s.durationMs, '9007199254740991') > 0 or
    type(s.isCompleted) ~= 'boolean' then error('INVALID_HISTORY') end
  return s
end
local function history_write(stateKey, orderKey, id, incoming)
  local raw = redis.call('HGET', stateKey, id)
  local old = raw and history_decode(raw)
  if old then
    if old.sourcePartition ~= incoming.sourcePartition then error('PARTITION_CHANGED') end
    local order = compare(old.sourceOffset, incoming.sourceOffset)
    if order > 0 or (order == 0 and old.confirmed and not incoming.confirmed) then return old end
    if not incoming.confirmed then incoming.createdAtUtc = old.createdAtUtc end
    redis.call('ZREM', orderKey, old.sortTime .. ':' .. string.gsub(id, '-', ''))
  end
  redis.call('HSET', stateKey, id, cjson.encode(incoming))
  redis.call('ZADD', orderKey, 0, incoming.sortTime .. ':' .. string.gsub(id, '-', ''))
  return incoming
end
