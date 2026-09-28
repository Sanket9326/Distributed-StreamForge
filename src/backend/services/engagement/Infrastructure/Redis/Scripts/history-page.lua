#!lua flags=no-writes
-- @common
expect(KEYS[1], 'hash'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'string')
if redis.call('GET', KEYS[3]) ~= '1' then return {0, false, {}} end
local limit = tonumber(ARGV[1])
if not limit or limit < 1 or limit > 50 then error('INVALID_LIMIT') end
local members = redis.call('ZRANGE', KEYS[2], ARGV[2] == '' and '+' or '(' .. ARGV[2], '-', 'BYLEX', 'REV', 'LIMIT', 0, limit + 1)
local items = {}; local cursor = false
for i = 1, math.min(#members, limit) do
  local id = string.sub(members[i], 21)
  id = string.sub(id,1,8)..'-'..string.sub(id,9,12)..'-'..string.sub(id,13,16)..'-'..string.sub(id,17,20)..'-'..string.sub(id,21)
  local raw = redis.call('HGET', KEYS[1], id)
  if not raw then error('HISTORY_INDEX_INCOMPLETE') end
  items[#items+1] = raw
end
if #members > limit then cursor = members[limit] end
return {1, cursor, items}
