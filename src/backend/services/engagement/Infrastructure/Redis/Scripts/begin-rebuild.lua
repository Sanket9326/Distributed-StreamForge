-- @common
if #ARGV ~= 1 or #ARGV[1] == 0 or #ARGV[1] > 64 then error('INVALID_TOKEN') end
expect(KEYS[1], 'string'); expect(KEYS[2], 'string'); expect(KEYS[3], 'string')
if redis.call('EXISTS', KEYS[3]) == 1 then return 2 end
if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', 30000) then
  redis.call('SET', KEYS[2], ARGV[1]); return 1
end
return 0
